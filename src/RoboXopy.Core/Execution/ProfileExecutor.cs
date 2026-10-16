using System.Diagnostics;
using System.Text;
using RoboXopy.Core.Insights;
using RoboXopy.Core.Models;
using RoboXopy.Core.Platform;
using RoboXopy.Core.Robocopy;
using RoboXopy.Core.Storage;

namespace RoboXopy.Core.Execution;

public sealed record RunRequest(SyncProfile Profile, RunTrigger Trigger, bool DryRun = false, bool ConfirmedHistoricalSourceDestination = false);

/// <summary>
/// Runs a profile end to end: cross-process locking, validation, robocopy execution, log file,
/// summary parsing, history and log retention.
/// </summary>
public sealed class ProfileExecutor(AppPaths paths, HistoryStore history, Func<AppSettings> settings)
{
    /// <summary>True when the profile is currently running in any RoboXopy process.</summary>
    public bool IsRunningAnywhere(Guid profileId)
    {
        var file = paths.LockFile(profileId);
        if (!File.Exists(file)) return false;
        try
        {
            using var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public async Task<RunRecord> ExecuteAsync(RunRequest request, Action<OutputLine>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var profile = request.Profile;
        var appSettings = settings();
        var arguments = RobocopyCommandBuilder.BuildArguments(profile, request.DryRun);
        var record = new RunRecord
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            Trigger = request.Trigger,
            DryRun = request.DryRun,
            StartedAt = DateTimeOffset.Now,
            CommandLine = "robocopy " + CommandLine.Join(arguments),
            PurgesExtras = profile.Options.Mode == CopyMode.Mirror || (profile.Options.Mode == CopyMode.Copy && profile.Options.Purge),
        };

        paths.EnsureCreated();
        if (FolderSafety.DestinationOverlapsSourceHistory(profile.Destination, appSettings.SourceFolderHistory)
            && !request.ConfirmedHistoricalSourceDestination
            && !FolderSafety.HasConfirmedHistoricalSourceDestination(profile))
        {
            return Finish(record, RobocopyExitCode.StartFailed, RunOutcome.Error,
                "The destination overlaps a folder previously used as a source. Run it interactively and type CONFIRM to continue.");
        }

        using var runLock = TryAcquireLock(profile.Id);
        if (runLock is null)
        {
            return Finish(record, RobocopyExitCode.StartFailed, RunOutcome.Skipped, "Skipped – this profile is already running.", persist: request.Trigger != RunTrigger.Manual);
        }

        var errors = ProfileValidator.Validate(profile).Where(i => i.Severity == IssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            return Finish(record, RobocopyExitCode.StartFailed, RunOutcome.Error, string.Join(" ", errors.Select(e => e.Message)));
        }

        var robocopy = appSettings.ResolveRobocopyPath();
        if (!File.Exists(robocopy))
        {
            return Finish(record, RobocopyExitCode.StartFailed, RunOutcome.Error, $"robocopy.exe was not found at \"{robocopy}\".");
        }

        IDisposable? scheduledRunLease;
        try
        {
            scheduledRunLease = request.Trigger is RunTrigger.Scheduled or RunTrigger.TaskScheduler
                ? await ScheduledRunLimiter.AcquireAsync(paths, appSettings, cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Finish(record, RobocopyExitCode.Cancelled, RunOutcome.Cancelled, "Cancelled while waiting for a scheduled run slot.");
        }

        using var scheduledRunLeaseScope = scheduledRunLease;

        var logDir = paths.ProfileLogsDirectory(profile.Id);
        Directory.CreateDirectory(logDir);
        record.LogFile = Path.Combine(logDir, $"{record.StartedAt:yyyyMMdd-HHmmss}{(request.DryRun ? "-preview" : "")}.log");

        await using var log = new StreamWriter(new FileStream(record.LogFile, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(true));
        var logSync = new object();
        WriteHeader(log, record, profile);

        var stopwatch = Stopwatch.StartNew();
        RobocopyProcessResult result;
        try
        {
            result = await RobocopyRunner.RunAsync(robocopy, arguments, line =>
            {
                var parsed = RobocopyOutputParser.Parse(line);
                if (parsed.Kind != OutputLineKind.Progress)
                {
                    lock (logSync) log.WriteLine(line);
                }

                onOutput?.Invoke(parsed);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            lock (logSync) log.WriteLine($"Failed to start robocopy: {ex.Message}");
            record.HasPermissionIssue = PermissionIssueDetector.IsAccessDenied(ex);
            return Finish(record, RobocopyExitCode.StartFailed, RunOutcome.Error, $"Failed to start robocopy: {ex.Message}");
        }

        record.Summary = RobocopyOutputParser.ParseSummary(result.Lines.Where(l => RobocopyOutputParser.Parse(l).Kind != OutputLineKind.Progress).ToList());
        var outcome = RobocopyExitCode.ToOutcome(result.ExitCode);
        var message = RobocopyExitCode.Describe(result.ExitCode, request.DryRun, record.PurgesExtras);
        record.Outcome = outcome;
        record.Findings = AnalyzeFindings(record);
        if (result.ExitCode != RobocopyExitCode.Cancelled
            && UnreadableFolders.Analyze(record, result.Lines.Select(RobocopyOutputParser.Parse), RedirectionGuard.IsEnforced, arguments) is { } unreadable)
        {
            (record.Findings ??= []).Insert(0, unreadable);
        }
        if (!request.DryRun && profile.Options.VerifyAfterCopy && (outcome is RunOutcome.Success or RunOutcome.Warning))
        {
            record.Verification = await VerifyAsync(robocopy, profile, record.PurgesExtras, log, logSync, cancellationToken).ConfigureAwait(false);
            if (record.Verification.Status != VerificationStatus.Passed)
            {
                var severity = FindingSeverity.Warning;
                (record.Findings ??= []).Add(new RunFinding
                {
                    Kind = RunFindingKind.Verification,
                    Severity = severity,
                    Message = record.Verification.Message,
                });
                if (record.Verification.ExitCode == RobocopyExitCode.Cancelled) outcome = RunOutcome.Cancelled;
                else if (outcome == RunOutcome.Success) outcome = RunOutcome.Warning;
                message += " · " + record.Verification.Message;
            }
        }
        record.HasPermissionIssue = PermissionIssueDetector.HasAccessDeniedOutput(result.Lines)
            || record.Findings?.Any(finding => finding.MayRequireElevation) == true;

        lock (logSync)
        {
            log.WriteLine();
            foreach (var finding in record.Findings ?? [])
            {
                var label = finding.Kind == RunFindingKind.UnreadableFolders ? "skipped"
                    : finding.Severity == FindingSeverity.Warning ? "unusual" : "note";
                log.WriteLine($"RoboXopy: {label} – {finding.Message}");
            }
            log.WriteLine($"RoboXopy: finished in {stopwatch.Elapsed:hh\\:mm\\:ss} with exit code {result.ExitCode} – {message}");
        }

        return Finish(record, result.ExitCode, outcome, message, retain: appSettings.LogRetentionCount);
    }

    private static async Task<VerificationResult> VerifyAsync(
        string robocopy,
        SyncProfile profile,
        bool purgesExtras,
        StreamWriter log,
        object logSync,
        CancellationToken cancellationToken)
    {
        var arguments = RobocopyCommandBuilder.BuildArguments(profile, dryRun: true);
        lock (logSync) log.WriteLine("RoboXopy: starting fast metadata verification (robocopy /L).");
        try
        {
            var result = await RobocopyRunner.RunAsync(robocopy, arguments, line =>
            {
                if (RobocopyOutputParser.Parse(line).Kind == OutputLineKind.Progress) return;
                lock (logSync) log.WriteLine($"Verification: {line}");
            }, cancellationToken).ConfigureAwait(false);
            var summary = RobocopyOutputParser.ParseSummary(result.Lines.Where(line => RobocopyOutputParser.Parse(line).Kind != OutputLineKind.Progress).ToList());
            var hasDifferences = HasRemainingDifferences(summary, purgesExtras, result.ExitCode);
            if (result.ExitCode >= 0 && result.ExitCode < 8 && !hasDifferences)
            {
                return new VerificationResult
                {
                    Status = VerificationStatus.Passed,
                    ExitCode = result.ExitCode,
                    Summary = summary,
                    Message = "Fast metadata verification passed.",
                };
            }

            var message = result.ExitCode == RobocopyExitCode.Cancelled
                ? "Fast metadata verification was cancelled."
                : result.ExitCode < 8
                    ? "Fast metadata verification found remaining differences."
                    : "Fast metadata verification could not complete.";
            return new VerificationResult
            {
                Status = result.ExitCode is > 0 and < 8 ? VerificationStatus.DifferencesFound : VerificationStatus.Failed,
                ExitCode = result.ExitCode,
                Summary = summary,
                Message = message,
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            lock (logSync) log.WriteLine($"RoboXopy: fast metadata verification could not start: {ex.Message}");
            return new VerificationResult
            {
                Status = VerificationStatus.Failed,
                ExitCode = RobocopyExitCode.StartFailed,
                Message = $"Fast metadata verification could not start: {ex.Message}",
            };
        }
    }

    private static bool HasRemainingDifferences(RobocopySummary? summary, bool purgesExtras, int exitCode)
    {
        if (summary is not null)
        {
            return summary.Files.Copied > 0
                || summary.Files.Mismatch > 0
                || summary.Files.Failed > 0
                || (purgesExtras && summary.Files.Extras > 0);
        }

        // /L exit code 2 only describes destination extras, which ordinary Copy mode intentionally keeps.
        var relevantBits = purgesExtras ? exitCode : exitCode & ~2;
        return (relevantBits & 7) != 0;
    }

    private List<RunFinding>? AnalyzeFindings(RunRecord record)
    {
        if (record.Summary is null) return null;
        try
        {
            return RunInsights.Analyze(record, history.Load(record.ProfileId, 50));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private RunRecord Finish(RunRecord record, int exitCode, RunOutcome outcome, string message, bool persist = true, int retain = 0)
    {
        record.EndedAt = DateTimeOffset.Now;
        record.ExitCode = exitCode;
        record.Outcome = outcome;
        record.Message = message;

        if (persist)
        {
            try
            {
                history.Append(record);
                if (retain > 0) history.ApplyLogRetention(record.ProfileId, retain);
            }
            catch (IOException)
            {
                // History is best effort; the run itself has completed.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return record;
    }

    private static void WriteHeader(StreamWriter log, RunRecord record, SyncProfile profile)
    {
        log.WriteLine($"RoboXopy – {profile.Name}{(record.DryRun ? " (preview / dry run)" : "")}");
        log.WriteLine($"Started : {record.StartedAt:yyyy-MM-dd HH:mm:ss} ({record.Trigger})");
        log.WriteLine($"Command : {record.CommandLine}");
        log.WriteLine();
    }

    private FileStream? TryAcquireLock(Guid profileId)
    {
        Directory.CreateDirectory(paths.LocksDirectory);
        try
        {
            var stream = new FileStream(paths.LockFile(profileId), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            var bytes = Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
            stream.Write(bytes);
            stream.Flush();
            return stream;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

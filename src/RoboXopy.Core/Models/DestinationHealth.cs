using System.Globalization;

namespace RoboXopy.Core.Models;

/// <summary>Destination availability and performance information captured for one run.</summary>
public sealed record DestinationHealthSnapshot
{
    public string Destination { get; init; } = "";
    public bool IsAvailable { get; init; }
    public string? AvailabilityError { get; init; }
    public long? AvailableFreeBytes { get; init; }
    public long? BytesCopied { get; init; }
    public double? BytesPerSecond { get; init; }
    public bool HasPermissionIssue { get; init; }

    public string Summary
    {
        get
        {
            if (!IsAvailable) return AvailabilityError is { Length: > 0 } error ? $"Unavailable: {error}" : "Destination unavailable";

            var parts = new List<string>();
            if (AvailableFreeBytes is { } free) parts.Add($"{FormatBytes(free)} free");
            if (BytesPerSecond is { } rate && rate > 0) parts.Add($"{FormatBytes((long)rate)}/s");
            if (HasPermissionIssue) parts.Add("permission issue");
            return parts.Count == 0 ? "Destination available" : string.Join(" · ", parts);
        }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {units[unit]}";
    }
}

/// <summary>Captures destination availability without creating or changing the destination path.</summary>
public static class DestinationHealthProbe
{
    public static DestinationHealthSnapshot Capture(string destination)
    {
        var normalized = FolderSafety.NormalizeFolder(destination);
        if (normalized is null) return new DestinationHealthSnapshot { Destination = destination, AvailabilityError = "The destination path is invalid." };

        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new DestinationHealthSnapshot
            {
                Destination = normalized,
                AvailabilityError = "The destination volume is unavailable.",
            };
        }

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return new DestinationHealthSnapshot
                {
                    Destination = normalized,
                    AvailabilityError = "The destination volume is not ready.",
                };
            }

            return new DestinationHealthSnapshot
            {
                Destination = normalized,
                IsAvailable = true,
                AvailableFreeBytes = drive.AvailableFreeSpace,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new DestinationHealthSnapshot
            {
                Destination = normalized,
                IsAvailable = true,
                AvailabilityError = $"Free space could not be inspected: {ex.Message}",
            };
        }
    }
}

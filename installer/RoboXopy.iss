; Inno Setup 7 script for RoboXopy.
;
; Built by tools\publish.ps1, which passes the defines below. To compile by hand, publish first and then run
;   ISCC.exe /DPublishDir=..\artifacts\RoboXopy-<version>-win-x64 installer\RoboXopy.iss
; AppVersion and FileVersion are then read from RoboXopy.exe.

#ifndef PublishDir
  #error Define PublishDir, the folder containing the published RoboXopy.exe (tools\publish.ps1 does this).
#endif

#define AppExe AddBackslash(PublishDir) + "RoboXopy.exe"
#if !FileExists(AppExe)
  #error RoboXopy.exe not found in PublishDir.
#endif

#ifndef FileVersion
  #define FileVersion GetVersionNumbersString(AppExe)
#endif

#ifndef AppVersion
  ; The product version is SemVer plus build metadata (1.2.0+build.14.20260518); drop the metadata.
  #define ProductVersion GetStringFileInfo(AppExe, PRODUCT_VERSION)
  #if Pos("+", ProductVersion) > 0
    #define AppVersion Copy(ProductVersion, 1, Pos("+", ProductVersion) - 1)
  #else
    #define AppVersion ProductVersion
  #endif
#endif

#ifndef Runtime
  #define Runtime "win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "RoboXopy-" + AppVersion + "-" + Runtime + "-setup"
#endif

[Setup]
; Identifies RoboXopy to Windows and to later versions of this installer: never change it.
AppId={{30B8CBFB-4468-4EB0-A6EA-1DBFC4F98B7A}
AppName=RoboXopy
AppVersion={#AppVersion}
AppPublisher=RoboXopy
AppCopyright=Copyright © 2026 RoboXopy
VersionInfoVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}

; Installs for the current user by default (no UAC prompt); the user can choose to install for all users.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\RoboXopy
DisableProgramGroupPage=yes
; The uninstaller may remove the user's RoboXopy data folder (see [Code]); that is intended.
UsedUserAreasWarning=no

#if Runtime == "win-x64"
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Runtime == "win-arm64"
; A 32-bit Setup, which also runs on Arm64 Windows 10 (no x64 emulation there).
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif Runtime == "win-x86"
ArchitecturesAllowed=x86compatible
#else
  #error Unsupported Runtime.
#endif
MinVersion=10.0.14393

; Close a running RoboXopy before replacing it, and start it again afterwards (the app registers for this).
; Setup keeps RedirectionGuard on; RoboXopy restarts itself through Explorer when it inherits it, because
; robocopy would otherwise skip junctions and symbolic links in the user's folders.
CloseApplications=yes
RestartApplications=yes

WizardStyle=modern dynamic windows11
SetupIconFile=..\src\RoboXopy\Assets\RoboXopy.ico
; One image per DPI step; Setup picks the best match. Regenerate with tools\make-installer-images.ps1.
WizardImageFile=images\large-202x386.png,images\large-269x515.png,images\large-336x643.png,images\large-403x772.png,images\large-430x824.png,images\large-498x953.png,images\large-534x1022.png
WizardSmallImageFile=images\small-58.png,images\small-77.png,images\small-97.png,images\small-116.png,images\small-124.png,images\small-143.png,images\small-159.png
UninstallDisplayIcon={app}\RoboXopy.exe
UninstallDisplayName=RoboXopy

OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.DeleteUserData=Do you also want to delete your RoboXopy profiles, settings, run history and logs?%n%n%1%n%nChoose No to keep them for a future installation.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\RoboXopy"; Filename: "{app}\RoboXopy.exe"; Comment: "Robocopy profiles, mirroring and schedules"
Name: "{autodesktop}\RoboXopy"; Filename: "{app}\RoboXopy.exe"; Comment: "Robocopy profiles, mirroring and schedules"; Tasks: desktopicon

[Run]
Filename: "{app}\RoboXopy.exe"; Description: "{cm:LaunchProgram,RoboXopy}"; Flags: nowait postinstall skipifsilent
; After an update started from inside RoboXopy (which passes /RELAUNCH=normal|minimized), start it again.
Filename: "{app}\RoboXopy.exe"; Parameters: "{code:RelaunchParams}"; Flags: nowait runasoriginaluser; Check: ShouldRelaunch

[UninstallRun]
; Close RoboXopy (the window app and any scheduled runs in progress) so its files can be removed.
Filename: "{app}\RoboXopy.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitRunning"
; Remove its Task Scheduler tasks and its "start with Windows" entry.
Filename: "{app}\RoboXopy.exe"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[UninstallDelete]
; Native libraries the single-file RoboXopy.exe extracts on first run.
Type: filesandordirs; Name: "{%TEMP}\.net\RoboXopy"

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|}') <> '';
end;

{ /RELAUNCH=minimized reopens RoboXopy in the notification area; /DATADIR passes on a non-standard data folder. }
function RelaunchParams(Param: String): String;
var
  DataDir: String;
begin
  Result := '';
  if CompareText(ExpandConstant('{param:RELAUNCH|}'), 'minimized') = 0 then
    Result := '--minimized';
  DataDir := ExpandConstant('{param:DATADIR|}');
  if DataDir <> '' then
  begin
    { A trailing backslash would escape the closing quote (e.g. a drive root such as D:\). }
    if DataDir[Length(DataDir)] = '\' then
      DataDir := DataDir + '\';
    Result := Trim(Result + ' --data-dir "' + DataDir + '"');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    DataDir := ExpandConstant('{userappdata}\RoboXopy');
    if DirExists(DataDir) and
       (MsgBox(FmtMessage(CustomMessage('DeleteUserData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(DataDir, True, True, True);
  end;
end;

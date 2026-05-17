; UsbEthUsb client - Inno Setup installer script.
;
; Build on Windows:
;   1. Install Inno Setup 6.3+        ->  https://jrsoftware.org/isdl.php
;   2. Publish the client (self-contained, so no .NET runtime needed on the target):
;        dotnet publish src\UsbEthUsb.Client\UsbEthUsb.Client.csproj ^
;          -c Release -r win-x64 --self-contained -o publish\client
;   3. Compile this script:
;        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" deploy\windows-installer.iss
;      (or open it in the Inno Setup IDE and press Build)
;   -> produces publish\UsbEthUsb-Client-Setup.exe
;
; All relative paths below are resolved from this .iss file's folder (deploy\).

#define AppName      "UsbEthUsb Client"
#define AppVersion   "1.0.0"
#define AppPublisher "Riaan Aspeling"
#define AppExeName   "UsbEthUsb.Client.exe"
#define RepoUrl      "https://github.com/RiaanAspeling/UsbEthUsb"

[Setup]
; Stable AppId - keep this constant so upgrades replace the previous version.
AppId={{34A69402-6B35-409F-8EE4-BFD166D6734C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
DefaultDirName={localappdata}\Programs\UsbEthUsb
DefaultGroupName=UsbEthUsb
DisableProgramGroupPage=yes
DisableDirPage=yes
; Per-user install - no admin/UAC (the client runs as the invoking user).
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Use the Restart Manager to close a running instance during install/upgrade,
; so the locked .exe never blocks the update.
CloseApplications=yes
RestartApplications=no
OutputDir=..\publish
OutputBaseFilename=UsbEthUsb-Client-Setup
SetupIconFile=..\src\UsbEthUsb.Client\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
SolidCompression=yes

[Files]
; The whole self-contained publish output.
Source: "..\publish\client\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Tasks]
Name: "startup"; Description: "Start UsbEthUsb automatically when I sign in"

[Icons]
Name: "{group}\UsbEthUsb";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall UsbEthUsb"; Filename: "{uninstallexe}"
Name: "{userstartup}\UsbEthUsb";     Filename: "{app}\{#AppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch UsbEthUsb now"; Flags: nowait postinstall skipifsilent

[Code]
{ --- usbip-win2 prerequisite check -------------------------------------------
  The client drives usbip-win2 (vhci driver + usbip.exe). If it isn't found we
  warn and offer the download page. This is best-effort: a custom install path
  with no registry entry could be missed, so a negative result never blocks the
  install. }

const
  UsbipWin2ReleasesUrl = 'https://github.com/vadimgrn/usbip-win2/releases';

{ True if any 'Uninstall' entry under the given root has a DisplayName mentioning usbip. }
function UninstallListHasUsbip(RootKey: Integer): Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
  DisplayName: String;
begin
  Result := False;
  if not RegGetSubkeyNames(RootKey,
       'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', Keys) then
    Exit;
  for I := 0 to GetArrayLength(Keys) - 1 do
  begin
    if RegQueryStringValue(RootKey,
         'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + Keys[I],
         'DisplayName', DisplayName) then
    begin
      if Pos('usbip', Lowercase(DisplayName)) > 0 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function IsUsbipWin2Installed(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf}\USBip\usbip.exe'))
         or FileExists(ExpandConstant('{commonpf32}\USBip\usbip.exe'))
         or UninstallListHasUsbip(HKLM64)
         or UninstallListHasUsbip(HKLM32);
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;   { never block the install — detection can yield false negatives }

  if not IsUsbipWin2Installed() then
  begin
    if SuppressibleMsgBox(
         'usbip-win2 does not appear to be installed.' + #13#10#13#10 +
         'The UsbEthUsb client requires it: usbip-win2 provides the virtual-USB '
         + 'driver and usbip.exe that the client drives. Without it, attaching a '
         + 'device will fail.' + #13#10#13#10 +
         'Open the usbip-win2 download page now?',
         mbConfirmation, MB_YESNO, IDNO) = IDYES then
    begin
      ShellExec('open', UsbipWin2ReleasesUrl, '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
  end;
end;

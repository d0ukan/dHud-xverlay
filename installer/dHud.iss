; dHud kurulum betiği (Inno Setup 6). Kullanıcı klasörüne kurulur, yönetici yetkisi istemez; böylece
; uygulama içi güncelleme (exe'yi kendi kendine değiştirme) yönetici olmadan da çalışır.
; Derleme: iscc /DAppVersion=1.0.0 /DSourceExe=..\bin\Release\publish\CpuGpuOverlay.exe installer\dHud.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\bin\Release\publish\CpuGpuOverlay.exe"
#endif

[Setup]
AppId={{6B0E3F52-5C61-4E0B-9B7B-2D3A8F7C1D40}
AppName=dHud
AppVersion={#AppVersion}
AppPublisher=d0ukan
AppPublisherURL=https://github.com/d0ukan/dHud-xverlay
DefaultDirName={localappdata}\Programs\dHud
DefaultGroupName=dHud
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\bin\Release\installer
OutputBaseFilename=dHud-Setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\CpuGpuOverlay.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Çalışan dHud kurulumun/kaldırmanın üstüne yazmasın.
AppMutex=Local\CpuGpuOverlay_SingleInstance
CloseApplications=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\dHud"; Filename: "{app}\CpuGpuOverlay.exe"
Name: "{group}\{cm:UninstallProgram,dHud}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\dHud"; Filename: "{app}\CpuGpuOverlay.exe"; Tasks: desktopicon

[Registry]
; Uygulama ayarlardan başlangıç kaydı oluşturduysa kaldırırken temizle (Registry yöntemi).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "CpuGpuOverlay"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\CpuGpuOverlay.exe"; Description: "{cm:LaunchProgram,dHud}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Görev Zamanlayıcı başlangıç görevi yönetici yetkisiyle oluşturulduysa bu komut yetkisiz çalışınca sessizce başarısız olur.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""CpuGpuOverlay"" /F"; Flags: runhidden; RunOnceId: "DeleteStartupTask"

[UninstallDelete]
; Uygulama içi güncellemeden kalan geçici dosyalar.
Type: files; Name: "{app}\*.old"
Type: files; Name: "{app}\*.new"

; Bobin Kumanda kurulum betiği (Inno Setup 6).
; Derleme: iscc /DAppVersion=0.1.0 /DArch=x64 /DSourceDir=..\artifacts\setup-x64 installer\BobinKumanda.iss
; Kurulan exe .NET çalışma ortamını içinde taşır; kurulum internet bağlantısı istemez.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\setup-" + Arch
#endif

#define AppName "Bobin Kumanda"
#define AppExe "BobinKumanda.exe"
#define RepoUrl "https://github.com/alperenyardim30-hash/BobinKumanda"

[Setup]
AppId={{EEBED0F5-013F-4B17-86B5-0DB2138215AC}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Bobin Kardeşler
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
AppCopyright=© 2026 Bahadır Değirmenci, Alperen Yardım — GPL-3.0
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Yönetici olmayan öğrenci hesabı da yalnızca kendisi için kurabilsin
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
MinVersion=10.0.14393
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputDir=..\artifacts\installer
OutputBaseFilename=BobinKumanda-{#AppVersion}-setup-{#Arch}
SetupIconFile=..\src\BobinKumanda.App\Assets\BobinKumanda.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
tr.AssocBobin=.bobin dosyalarını Bobin Kumanda ile aç
en.AssocBobin=Open .bobin files with Bobin Kumanda
tr.AssocEsc=Eski EKTS .esc dosyalarını Bobin Kumanda ile aç
en.AssocEsc=Open old EKTS .esc files with Bobin Kumanda
tr.FileTypes=Dosya ilişkilendirme:
en.FileTypes=File associations:
tr.BobinFile=Bobin Kumanda devresi
en.BobinFile=Bobin Kumanda circuit
tr.EscFile=EKTS devresi
en.EscFile=EKTS circuit

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assocbobin"; Description: "{cm:AssocBobin}"; GroupDescription: "{cm:FileTypes}"
; EKTS de kuruluysa onun dosya ilişkisini ezmemek için varsayılan olarak kapalı
Name: "assocesc"; Description: "{cm:AssocEsc}"; GroupDescription: "{cm:FileTypes}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.bobin"; ValueType: string; ValueName: ""; ValueData: "BobinKumanda.Circuit"; Flags: uninsdeletevalue; Tasks: assocbobin
Root: HKA; Subkey: "Software\Classes\.bobin\OpenWithProgids"; ValueType: string; ValueName: "BobinKumanda.Circuit"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocbobin
Root: HKA; Subkey: "Software\Classes\BobinKumanda.Circuit"; ValueType: string; ValueName: ""; ValueData: "{cm:BobinFile}"; Flags: uninsdeletekey; Tasks: assocbobin
Root: HKA; Subkey: "Software\Classes\BobinKumanda.Circuit\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assocbobin
Root: HKA; Subkey: "Software\Classes\BobinKumanda.Circuit\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assocbobin
Root: HKA; Subkey: "Software\Classes\.esc"; ValueType: string; ValueName: ""; ValueData: "BobinKumanda.Esc"; Flags: uninsdeletevalue; Tasks: assocesc
Root: HKA; Subkey: "Software\Classes\.esc\OpenWithProgids"; ValueType: string; ValueName: "BobinKumanda.Esc"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocesc
Root: HKA; Subkey: "Software\Classes\BobinKumanda.Esc"; ValueType: string; ValueName: ""; ValueData: "{cm:EscFile}"; Flags: uninsdeletekey; Tasks: assocesc
Root: HKA; Subkey: "Software\Classes\BobinKumanda.Esc\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assocesc
Root: HKA; Subkey: "Software\Classes\BobinKumanda.Esc\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assocesc
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".bobin"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".esc"; ValueData: ""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

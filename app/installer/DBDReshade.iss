; Installer for DBD Reshade (Inno Setup 6).
; Built by .github/workflows/build-app.yml:
;   iscc /DMyAppVersion=2.0.6 /DSourceExe=<path to published DBDReshade.exe> /DOutputDir=<folder> DBDReshade.iss
; To build it by hand: from the app folder run
;   dotnet publish src\DBDReshade\DBDReshade.csproj -c Release -o publish
; then open this file in Inno Setup and press Compile (the defaults below point at app\publish).

#ifndef MyAppVersion
  #define MyAppVersion "2.0.6"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\DBDReshade.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

#define MyAppName "DBD Reshade"
#define MyAppExe "DBDReshade.exe"

[Setup]
; AppId identifies the app for updates and uninstall: never change it.
AppId={{3783DA61-BCD9-4E05-86D7-C78C4D8F3454}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Joolace
AppPublisherURL=https://github.com/Joolace/dbd-reshade
AppSupportURL=https://discord.gg/mC7Eabu3QW
AppUpdatesURL=https://github.com/Joolace/dbd-reshade/releases
VersionInfoVersion={#MyAppVersion}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; The app needs administrator rights anyway (it writes into the game folder).
PrivilegesRequired=admin
; Uninstall asks whether to delete %LOCALAPPDATA%\DBDReshade: that's intended.
UsedUserAreasWarning=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

OutputDir={#OutputDir}
OutputBaseFilename=DBDReshade-Setup
SetupIconFile=..\src\DBDReshade\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; Updates run this installer while the app may still be closing: let Windows close it.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.DeleteData=Also delete your presets, settings and downloaded files?%n%nChoose No to keep them for a future installation.
italian.DeleteData=Eliminare anche i tuoi preset, le impostazioni e i file scaricati?%n%nScegli No per conservarli per una futura installazione.
spanish.DeleteData=¿Eliminar también tus presets, ajustes y archivos descargados?%n%nElige No para conservarlos para una futura instalación.
french.DeleteData=Supprimer aussi vos presets, paramètres et fichiers téléchargés ?%n%nChoisissez Non pour les garder pour une prochaine installation.
german.DeleteData=Auch deine Presets, Einstellungen und heruntergeladenen Dateien löschen?%n%nWähle Nein, um sie für eine spätere Installation zu behalten.
brazilianportuguese.DeleteData=Excluir também seus presets, configurações e arquivos baixados?%n%nEscolha Não para mantê-los para uma futura instalação.
russian.DeleteData=Удалить также ваши пресеты, настройки и загруженные файлы?%n%nВыберите «Нет», чтобы сохранить их для будущей установки.
japanese.DeleteData=プリセット、設定、ダウンロードしたファイルも削除しますか？%n%n今後また使う場合は「いいえ」を選んでください。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; Normal install: offer to start the app at the end.
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser
; Update from inside the app (runs /SILENT): start the new version automatically.
Filename: "{app}\{#MyAppExe}"; Flags: nowait runascurrentuser; Check: WizardSilent

[UninstallDelete]
; The map preview copy of the app and its ReShade live in the data folder; the [Code] below asks about it.
Type: files; Name: "{app}\{#MyAppExe}.old"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\DBDReshade'), True, True, True);
end;

; Instalador do YtCortador. Compile com: iscc /DAppVersion=1.2.3 instalador.iss  (depois do dotnet publish self-contained)
; A versão vem da tag do git (o workflow de release passa /DAppVersion); build local fica como 0.0.0-dev.
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
[Setup]
AppName=YtCortador
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\YtCortador
DefaultGroupName=YtCortador
; Instala só para o usuário: sem admin, e o winget instala as dependências no perfil certo.
PrivilegesRequired=lowest
OutputDir=instalador
OutputBaseFilename=YtCortador-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\YtCortador.exe

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktop"; Description: "Criar atalho na área de trabalho"

[Files]
Source: "publish-self-contained\YtCortador.exe"; DestDir: "{app}"

[Icons]
Name: "{group}\YtCortador"; Filename: "{app}\YtCortador.exe"
Name: "{userdesktop}\YtCortador"; Filename: "{app}\YtCortador.exe"; Tasks: desktop

[Run]
Filename: "{code:Winget}"; Parameters: "install --id yt-dlp.yt-dlp -e --silent --accept-package-agreements --accept-source-agreements"; StatusMsg: "Instalando yt-dlp..."; Flags: runhidden; Check: TemWinget
Filename: "{code:Winget}"; Parameters: "install --id yt-dlp.FFmpeg -e --silent --accept-package-agreements --accept-source-agreements"; StatusMsg: "Instalando ffmpeg..."; Flags: runhidden; Check: TemWinget
Filename: "{app}\YtCortador.exe"; Description: "Abrir o YtCortador"; Flags: nowait postinstall skipifsilent

[Code]
function Winget(Param: String): String;
begin
  Result := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
end;

function TemWinget: Boolean;
begin
  Result := FileExists(Winget(''));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not TemWinget then
    MsgBox('O winget não foi encontrado. Instale o "Instalador de Aplicativo" pela Microsoft Store e depois rode: winget install yt-dlp.yt-dlp yt-dlp.FFmpeg', mbInformation, MB_OK);
end;

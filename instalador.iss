; Instalador do YtCortador. Compile com: iscc /DAppVersion=1.2.3 instalador.iss  (depois do dotnet publish self-contained)
; A versão vem da tag do git (o workflow de release passa /DAppVersion); build local fica como 0.0.0-dev.
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
[Setup]
; AppId fixo: é por ele que o Windows reconhece uma instalação anterior e atualiza no lugar.
; Igual ao AppName de antes (padrão do Inno), então quem tem versões antigas continua atualizando.
; NUNCA mude este valor; o AppName pode mudar à vontade.
AppId=YtCortador
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
// Compara "1.2.3" com "1.10.0" numericamente; ignora sufixo de pré-release ("-rc.1", "-dev")
// e trata partes faltando como 0 ("1.0" = "1.0.0", formato das instalações antigas). Retorna -1, 0 ou 1.
function ParteVersao(var V: String): Integer;
var
  P: Integer;
begin
  P := Pos('.', V);
  if P = 0 then
  begin
    Result := StrToIntDef(V, 0);
    V := '';
  end
  else
  begin
    Result := StrToIntDef(Copy(V, 1, P - 1), 0);
    Delete(V, 1, P);
  end;
end;

function CompararVersoes(A, B: String): Integer;
var
  I, X, Y, P: Integer;
begin
  P := Pos('-', A); if P > 0 then A := Copy(A, 1, P - 1);
  P := Pos('-', B); if P > 0 then B := Copy(B, 1, P - 1);
  Result := 0;
  for I := 1 to 3 do
  begin
    X := ParteVersao(A);
    Y := ParteVersao(B);
    if X <> Y then
    begin
      if X > Y then Result := 1 else Result := -1;
      Exit;
    end;
  end;
end;

#ifdef TESTE
// Autoteste: iscc /DTESTE instalador.iss e rode o setup com /SILENT /resultado=<arquivo>.
// Grava "ok" ou "falhou" no arquivo e nunca instala nada.
function TestarVersoes: Boolean;
var
  Ok: Boolean;
begin
  Ok := (CompararVersoes('1.0.1', '1.0.0') = 1) and (CompararVersoes('1.0', '1.0.0') = 0)
    and (CompararVersoes('1.0.0', '1.1.0-rc.1') = -1) and (CompararVersoes('1.10.0', '1.9.0') = 1)
    and (CompararVersoes('0.0.0-dev', '1.0.0') = -1) and (CompararVersoes('2.0.0', '1.99.99') = 1)
    and (CompararVersoes('1.1.0-rc.2', '1.1.0') = 0);
  if Ok then
    SaveStringToFile(ExpandConstant('{param:resultado}'), 'ok', False)
  else
    SaveStringToFile(ExpandConstant('{param:resultado}'), 'falhou', False);
  Result := False;
end;
#endif

// Evita downgrade acidental: se já há uma versão mais nova instalada, pergunta antes (padrão: Não).
// Em instalação silenciosa vale a resposta padrão, então o downgrade não acontece.
function InitializeSetup: Boolean;
var
  Instalada: String;
begin
#ifdef TESTE
  Result := TestarVersoes;
  Exit;
#endif
  Result := True;
  if RegQueryStringValue(HKA, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1', 'DisplayVersion', Instalada)
    and (CompararVersoes(Instalada, '{#AppVersion}') > 0) then
    Result := SuppressibleMsgBox(FmtMessage('A versão %1 do YtCortador já está instalada e é mais nova que esta (%2).' + #13#10#13#10 +
      'Instalar a versão mais antiga por cima?', [Instalada, '{#AppVersion}']), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

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

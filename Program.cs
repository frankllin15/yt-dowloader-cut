using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class Program
{
    [STAThread] // WebView2 e os diálogos do Windows exigem STA; top-level statements não aceitam o atributo.
    static void Main(string[] args)
    {
        // O PATH herdado pode estar desatualizado (programas instalados pelo winget depois que o Explorer/VS abriu).
        Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
            + ";" + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
        // Uso direto: YtCortador.exe --miniatura "Salmo 142 - Com a minha voz" saida.png
        if (args is ["--miniatura", var musica, var png]) { Miniatura.Gerar(musica, png); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    // Paleta das miniaturas do Tom de Louvor: verde profundo + ouro.
    static readonly Color Fundo = Color.FromArgb(5, 36, 25), Superficie = Color.FromArgb(9, 46, 33),
        Campo = Color.FromArgb(13, 58, 42), Borda = Color.FromArgb(30, 86, 64), Selecao = Color.FromArgb(22, 92, 66),
        Ouro = Color.FromArgb(214, 180, 106), OuroClaro = Color.FromArgb(232, 203, 138),
        Texto = Color.FromArgb(242, 239, 230), Apagado = Color.FromArgb(143, 168, 156), Erro = Color.FromArgb(229, 138, 122),
        Abismo = Color.FromArgb(2, 22, 15);

    readonly TextBox url = new() { Dock = DockStyle.Fill, PlaceholderText = "Cole o link do YouTube e tecle Enter" };
    readonly TextBox pasta = new() { Dock = DockStyle.Fill, Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.RowHeaderSelect, MultiSelect = false, RowHeadersWidth = 28,
    };
    readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly Button iniciar = Botao("BAIXAR CORTES", Estilo.Primario);
    readonly CheckBox miniatura = new() { Text = "Gerar miniatura — cada corte numa pasta com seu nome", AutoSize = true, Checked = true, Anchor = AnchorStyles.Left };
    readonly Barra barra = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 6, BackColor = Campo, ForeColor = Ouro, Margin = new Padding(12, 0, 0, 0) };
    readonly Label status = Rotulo("Abra um vídeo, dê play e marque os cortes.", Ui(9.5f, "Medium"), Apagado);
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Abismo };

    Control[] edicao = [];
    Process? atual;
    bool rodando, cancelado;

    // Aceita "SS", "MM:SS" ou "HH:MM:SS" (com fração opcional), como o ffmpeg.
    static readonly Regex Tempo = new(@"^\d+(:[0-5]?\d){0,2}(\.\d+)?$");
    const string JsVideo = "document.querySelector('video')";

    // Usa a página normal do YouTube (embed falha em vídeos que bloqueiam incorporação) e esconde
    // tudo menos o player, esticado na janela. O resize faz o YouTube recalcular o tamanho do vídeo.
    const string SoPlayer = """
        if (location.hostname.endsWith('youtube.com')) {
          const css = `
            html, body, ytd-app { overflow: hidden !important; background: #000 !important; }
            #movie_player { position: fixed !important; inset: 0 !important; width: 100vw !important;
                            height: 100vh !important; z-index: 2147483647 !important; background: #000 !important; }
            #masthead-container, tp-yt-app-drawer, ytd-mini-guide-renderer, #secondary, #below,
            ytd-popup-container, tp-yt-iron-overlay-backdrop, .html5-endscreen, .ytp-ce-element,
            #movie_player [class*="endscreen"], #movie_player [class*="videowall"], .ytp-fullscreen-grid { display: none !important; }
            #movie_player .html5-video-container { position: absolute !important; inset: 0 !important;
                                                   width: 100% !important; height: 100% !important; }
            #movie_player video { position: absolute !important; width: 100% !important; height: 100% !important; left: 0 !important;
                                  top: 0 !important; object-fit: contain !important; }`;
          // Roda na criação do documento, antes de existir <html>: espera o DOM.
          document.addEventListener('DOMContentLoaded', () => {
            const s = document.createElement('style'); s.textContent = css;
            document.head.appendChild(s);
            const r = () => window.dispatchEvent(new Event('resize'));
            for (const t of [300, 1500, 3000]) setTimeout(r, t);
            document.addEventListener('yt-navigate-finish', r);
          });
          // Sem autoplay: ao terminar, cancela o "próximo vídeo" para o preview não trocar de vídeo sozinho.
          document.addEventListener('ended', () => {
            for (const t of [0, 300, 1000]) setTimeout(() => document.querySelector('.ytp-autonav-endscreen-upnext-cancel-button')?.click(), t);
          }, true);
        }
        """;

    static MainForm()
    {
        Debug.Assert(Segundos("11606") == 4566 && Segundos("130") == 90 && Segundos("45") == 45 && Segundos("1:30,5") == 90.5);
        Debug.Assert(Segundos("1:75") == null && Segundos("abc") == null && Segundos("1234567") == null);
        Debug.Assert(Formatar(4566) == "01:16:06" && Formatar(90.54) == "00:01:30.5");
        Debug.Assert(Limpar("Con") == "_Con" && Limpar("nul.x") == "_nul.x" && Limpar("Console") == "Console");
        Debug.Assert(Limpar(" Aleluia... ") == "Aleluia" && Limpar("...") == "" && Limpar("Salmo 23?") == "Salmo 23");
    }

    public MainForm()
    {
        Text = "Estúdio de cortes · Tom de Louvor";
        Width = 1320; Height = 860;
        MinimumSize = new Size(1040, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Fundo; ForeColor = Texto;
        Font = new Font("Segoe UI", 9.5f);

        grid.Columns.Add("nome", "NOME DO CORTE");
        grid.Columns.Add("inicio", "INÍCIO");
        grid.Columns.Add("fim", "FIM");
        grid.Columns.Add("status", "STATUS");
        grid.Columns[0].FillWeight = 230;
        grid.Columns[3].ReadOnly = true;
        var mono = new Font("Consolas", 10.5f);
        foreach (var c in new[] { grid.Columns[1], grid.Columns[2] })
        {
            c.DefaultCellStyle.Font = mono;
            c.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells; // acompanha a escala de DPI
            c.MinimumWidth = 90;
        }
        grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells; // o nome fica com o resto
        grid.Columns[3].MinimumWidth = 96;
        grid.CellEndEdit += (_, e) => NormalizarTempo(e.RowIndex, e.ColumnIndex);
        EstilizarGrade();

        var abrir = Botao("Abrir", Estilo.Normal);
        abrir.Click += (_, _) => AbrirVideo();
        url.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AbrirVideo(); } };

        var marcarIni = Botao("Marcar início", Estilo.Destaque);
        var marcarFim = Botao("Marcar fim", Estilo.Destaque);
        var irPara = Botao("Ir ao início", Estilo.Normal);
        var remover = Botao("Remover", Estilo.Normal);
        remover.Click += (_, _) => { if (grid.CurrentRow is { IsNewRow: false } r) grid.Rows.Remove(r); };
        marcarIni.Click += async (_, _) => await Marcar(inicio: true);
        marcarFim.Click += async (_, _) => await Marcar(inicio: false);
        irPara.Click += async (_, _) => await IrPara();
        var botoesTempo = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 6) };
        botoesTempo.Controls.AddRange([marcarIni, marcarFim, irPara, remover]);
        var dica = Rotulo("Ou digite só números na tabela:  11606 → 01:16:06  ·  130 → 01:30", Ui(8.5f, "Medium"), Apagado, new Padding(0, 2, 0, 8));

        var procurar = Botao("Escolher…", Estilo.Normal);
        procurar.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = pasta.Text };
            if (d.ShowDialog() == DialogResult.OK) pasta.Text = d.SelectedPath;
        };
        iniciar.Click += async (_, _) =>
        {
            if (!rodando) { await Executar(); return; }
            cancelado = true;
            Matar();
        };
        FormClosing += (_, e) =>
        {
            if (!rodando) return;
            if (MessageBox.Show("Há cortes sendo baixados. Cancelar e sair?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
            cancelado = true;
            Matar(); // senão yt-dlp/ffmpeg seguem rodando escondidos
        };

        miniatura.FlatStyle = FlatStyle.Flat;
        miniatura.FlatAppearance.BorderColor = Ouro;
        miniatura.FlatAppearance.CheckedBackColor = Ouro;
        miniatura.ForeColor = Texto;
        miniatura.Font = Ui(9, "Medium");
        miniatura.UseCompatibleTextRendering = true;
        miniatura.Margin = new Padding(0, 2, 0, 12);

        foreach (var tb in new[] { url, pasta })
        {
            tb.BorderStyle = BorderStyle.None; tb.BackColor = Campo; tb.ForeColor = Texto; tb.Font = new Font("Segoe UI", 10.5f);
        }
        log.BorderStyle = BorderStyle.None; log.BackColor = Abismo; log.ForeColor = Apagado; log.Font = new Font("Consolas", 8.5f);

        // Cabeçalho no estilo da miniatura: rótulo dourado espaçado, título pesado e o fio de ouro.
        var cabecalho = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        cabecalho.Controls.Add(Rotulo(Esp("TOM DE LOUVOR"), Ui(8.5f, "SemiBold"), Ouro, new Padding(2, 0, 0, 2)));
        cabecalho.Controls.Add(Rotulo("ESTÚDIO DE CORTES", Ui(19, "ExtraBold"), Texto));
        cabecalho.Controls.Add(new Panel { Size = new Size(54, 4), BackColor = Ouro, Margin = new Padding(4, 6, 0, 0) });

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(26, 18, 26, 14), BackColor = Fundo };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Linha(Control c, int col = 0, int span = 2, SizeType tipo = SizeType.AutoSize, float h = 0)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(tipo, h));
            t.Controls.Add(c, col, row); t.SetColumnSpan(c, span);
        }
        void Par(Control a, Control b) { Linha(a, 0, 1); t.Controls.Add(b, 1, t.RowCount - 1); }

        Linha(cabecalho);
        Linha(Secao("01", "VÍDEO"));
        Par(Caixa(url), abrir);
        Linha(Secao("02", "CORTES"));
        Linha(botoesTempo);
        Linha(dica);
        Linha(Moldura(grid), tipo: SizeType.Percent, h: 100);
        Linha(Secao("03", "EXPORTAR"));
        Par(Caixa(pasta), procurar);
        Linha(miniatura);
        var acao = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
        acao.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        acao.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        acao.Controls.Add(iniciar, 0, 0); acao.Controls.Add(barra, 1, 0);
        Linha(acao);
        status.Margin = new Padding(0, 8, 0, 6);
        status.AutoSize = false; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.Height = 24;
        Linha(status);
        Linha(log, tipo: SizeType.Absolute, h: 60);

        edicao = [url, abrir, botoesTempo, pasta, procurar, miniatura];
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Borda, SplitterWidth = 2 };
        split.Panel1.BackColor = Abismo;
        split.Panel2.BackColor = Fundo;
        split.Panel1.Controls.Add(web);
        split.Panel2.Controls.Add(t);
        Controls.Add(split);
        Load += async (_, _) =>
        {
            split.SplitterDistance = ClientSize.Width * 52 / 100;
            foreach (var c in new Control[] { grid, log }.Concat(grid.Controls.OfType<ScrollBar>()))
                SetWindowTheme(c.Handle, "DarkMode_Explorer", null); // barras de rolagem escuras
            try
            {
                // Pasta própria: ao lado do exe nem sempre dá para escrever.
                var dados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtCortador", "WebView2");
                await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, dados));
                await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(SoPlayer);
                web.CoreWebView2.NavigateToString(Capa());
            }
            catch (Exception ex)
            {
                split.Panel1.Controls.Clear();
                split.Panel1.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Apagado,
                    Text = "Preview indisponível: instale o Microsoft Edge WebView2 Runtime.\n" + ex.Message });
            }
        };
    }

    // Barra de título escura no verde da marca (Windows 11; no 10 só o modo escuro funciona).
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int escuro = 1, legenda = ColorTranslator.ToWin32(Fundo), borda = ColorTranslator.ToWin32(Borda);
        DwmSetWindowAttribute(Handle, 20, ref escuro, 4);
        DwmSetWindowAttribute(Handle, 35, ref legenda, 4);
        DwmSetWindowAttribute(Handle, 34, ref borda, 4);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int valor, int tam);
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string? id);

    void EstilizarGrade()
    {
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Superficie;
        grid.GridColor = Borda;
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = 36;
        grid.RowTemplate.Height = 34;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Fundo, ForeColor = Ouro, SelectionBackColor = Fundo, SelectionForeColor = Ouro,
            Font = new Font("Segoe UI Semibold", 8.25f), Padding = new Padding(8, 0, 0, 0),
        };
        grid.RowHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Superficie, SelectionBackColor = Ouro, ForeColor = Apagado };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Superficie, ForeColor = Texto, SelectionBackColor = Selecao, SelectionForeColor = Color.White,
            Font = new Font("Segoe UI", 10), Padding = new Padding(8, 0, 4, 0),
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Campo;
        // Status colorido: ouro = pronto, coral = erro, apagado = em andamento.
        grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 3 || e.Value is not string s || e.CellStyle == null) return;
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = s.StartsWith('✓') ? Ouro : s.StartsWith('✗') ? Erro : Apagado;
        };
    }

    enum Estilo { Normal, Destaque, Primario }

    static Font Ui(float pt, string peso = "SemiBold") => Miniatura.Fonte("Montserrat " + peso, pt, GraphicsUnit.Point);

    // UseCompatibleTextRendering: desenha com GDI+, o único que enxerga a Montserrat embutida.
    static Label Rotulo(string texto, Font f, Color cor, Padding? margem = null) =>
        new() { Text = texto, AutoSize = true, Font = f, ForeColor = cor, UseCompatibleTextRendering = true, Margin = margem ?? new Padding(0) };

    static string Esp(string s) => string.Join(' ', s.ToCharArray()); // espaçamento entre letras, como na miniatura

    static Control Secao(string numero, string nome)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 6) };
        p.Controls.Add(Rotulo(numero, Ui(8.5f, "ExtraBold"), Ouro, new Padding(0, 0, 6, 0)));
        p.Controls.Add(Rotulo(Esp(nome), Ui(8.5f, "SemiBold"), Texto));
        return p;
    }

    static Button Botao(string texto, Estilo estilo)
    {
        var b = new Button
        {
            Text = texto, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseCompatibleTextRendering = true,
            Font = Ui(estilo == Estilo.Primario ? 10 : 9, estilo == Estilo.Primario ? "ExtraBold" : "SemiBold"),
            Padding = estilo == Estilo.Primario ? new Padding(18, 7, 18, 7) : new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 0, 6, 0),
            BackColor = estilo == Estilo.Primario ? Ouro : Campo,
            ForeColor = estilo switch { Estilo.Primario => Fundo, Estilo.Destaque => Ouro, _ => Texto },
        };
        b.FlatAppearance.BorderColor = estilo == Estilo.Normal ? Borda : Ouro;
        b.FlatAppearance.MouseOverBackColor = estilo == Estilo.Primario ? OuroClaro : Borda;
        b.FlatAppearance.MouseDownBackColor = estilo == Estilo.Primario ? Ouro : Selecao;
        return b;
    }

    // TextBox sem borda dentro de um painel com respiro: o FixedSingle nativo fica claro no tema escuro.
    static Control Caixa(TextBox tb) =>
        new Panel { Dock = DockStyle.Fill, Height = tb.PreferredHeight + 14, Padding = new Padding(10, 7, 10, 0), BackColor = Campo, Margin = new Padding(0, 0, 8, 10), Controls = { tb } };

    static Control Moldura(Control c) =>
        new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Borda, Margin = new Padding(0, 0, 0, 16), Controls = { c } };

    // Tela vazia do preview: a mesma linguagem da miniatura (verde com luz, cruz d'água, fio de ouro).
    static string Capa()
    {
        string fonte = Convert.ToBase64String(Miniatura.Recurso("Montserrat.ttf"));
        return $$"""
            <!doctype html><meta charset="utf-8"><style>
            @font-face { font-family: M; src: url(data:font/ttf;base64,{{fonte}}) format('truetype'); font-weight: 100 900; }
            html, body { margin: 0; height: 100%; overflow: hidden; }
            body { font-family: M, 'Segoe UI', sans-serif; color: #f2efe6; display: flex; align-items: center;
                   background: radial-gradient(ellipse 70% 80% at 85% 10%, #105c3e 0%, #052419 60%, #02160f 100%); }
            .cruz::before, .cruz::after { content: ''; position: absolute; background: rgba(255,255,255,.06); }
            .cruz::before { right: 13%; top: -6%; width: 9vh; height: 95vh; }
            .cruz::after { right: calc(13% - 14vh); top: 14vh; width: 37vh; height: 9vh; }
            main { position: relative; padding: 0 9%; animation: sobe .9s cubic-bezier(.2,.7,.2,1) both; }
            .rot { color: #d6b46a; font-weight: 600; font-size: 13px; letter-spacing: .38em; }
            h1 { font-weight: 800; font-size: clamp(26px, 4.4vw, 54px); line-height: 1.04; margin: 14px 0 22px; text-transform: uppercase; }
            .fio { width: 72px; height: 6px; background: #d6b46a; animation: fio 1.1s .4s cubic-bezier(.2,.7,.2,1) both; }
            p { color: #8fa89c; font-weight: 500; font-size: 15px; line-height: 1.6; max-width: 30em; margin-top: 24px; }
            b { color: #f2efe6; font-weight: 600; }
            @keyframes sobe { from { opacity: 0; transform: translateY(14px); } }
            @keyframes fio { from { width: 0; } }
            </style>
            <div class="cruz"></div>
            <main>
              <div class="rot">PREVIEW</div>
              <h1>Cole o link<br>do vídeo ao lado</h1>
              <div class="fio"></div>
              <p>Dê play, pause no ponto certo e use <b>Marcar início</b> e <b>Marcar fim</b>. Cada corte vira um vídeo — com miniatura pronta para o YouTube.</p>
            </main>
            """;
    }

    void AbrirVideo()
    {
        if (!Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
        {
            MessageBox.Show("Cole um link válido do YouTube.");
            return;
        }
        web.CoreWebView2?.Navigate(u.ToString());
    }

    async Task<double?> TempoDoPlayer()
    {
        if (web.CoreWebView2 == null) return null;
        // Durante anúncio o <video> toca o anúncio; o YouTube marca o player com .ad-showing.
        string r = await web.ExecuteScriptAsync($"(() => {{ const v = {JsVideo}; return !v ? null : document.querySelector('.ad-showing') ? 'ad' : v.currentTime; }})()");
        if (r == "\"ad\"") { MessageBox.Show("Está passando um anúncio. Espere ele acabar (ou pule) e marque de novo."); return -1; }
        return double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null;
    }

    async Task Marcar(bool inicio)
    {
        var s0 = await TempoDoPlayer();
        if (s0 is < 0) return; // anúncio, já avisado
        if (s0 is not { } s) { MessageBox.Show("Abra o vídeo no preview primeiro."); return; }
        var atualLinha = grid.CurrentRow is { IsNewRow: false } r ? r : null;
        DataGridViewRow linha;
        if (inicio)
        {
            // Reaproveita a linha atual se ela ainda não foi fechada; senão começa um corte novo.
            linha = atualLinha != null && Vazio(atualLinha.Cells[2]) ? atualLinha : grid.Rows[grid.Rows.Add()];
            linha.Cells[1].Value = Formatar(s);
        }
        else
        {
            if (atualLinha == null) { MessageBox.Show("Marque o início primeiro."); return; }
            linha = atualLinha;
            linha.Cells[2].Value = Formatar(s);
        }
        linha.Cells[1].ErrorText = linha.Cells[2].ErrorText = "";
        grid.Focus();
        grid.CurrentCell = linha.Cells[!inicio && Vazio(linha.Cells[0]) ? 0 : inicio ? 1 : 2];
        if (!inicio && Vazio(linha.Cells[0])) grid.BeginEdit(true); // pede o nome logo após fechar o corte
    }

    async Task IrPara()
    {
        if (grid.CurrentRow is not { IsNewRow: false } r || Segundos(r.Cells[1].Value?.ToString()) is not { } s || web.CoreWebView2 == null) return;
        await web.ExecuteScriptAsync($"{JsVideo}.currentTime = {s.ToString(CultureInfo.InvariantCulture)}");
    }

    static bool Vazio(DataGridViewCell c) => string.IsNullOrWhiteSpace(c.Value?.ToString());

    void NormalizarTempo(int row, int col)
    {
        if (col is not (1 or 2) || row < 0) return;
        var c = grid.Rows[row].Cells[col];
        if (Vazio(c)) { c.ErrorText = ""; return; }
        if (Segundos(c.Value?.ToString()) is { } s) { c.Value = Formatar(s); c.ErrorText = ""; }
        else c.ErrorText = "Tempo inválido. Ex.: 01:16:06, 1:30 ou 11606";
    }

    /// "11606" vira 01:16:06 (pares da direita p/ esquerda); aceita também SS, MM:SS, HH:MM:SS e vírgula decimal.
    static double? Segundos(string? s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        if (s.Length > 2 && s.All(char.IsDigit))
            s = string.Join(":", (s.Length % 2 == 1 ? "0" + s : s).Chunk(2).Select(c => new string(c)));
        if (!Tempo.IsMatch(s)) return null;
        double t = 0;
        foreach (var p in s.Split(':')) t = t * 60 + double.Parse(p, CultureInfo.InvariantCulture);
        return t;
    }

    static string Formatar(double s)
    {
        var ts = TimeSpan.FromSeconds(Math.Round(s, 1));
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}" + (ts.Milliseconds > 0 ? $".{ts.Milliseconds / 100}" : "");
    }

    async Task Executar()
    {
        grid.EndEdit();
        var cortes = new List<(DataGridViewRow linha, string nome, string titulo, double ini, double fim)>();
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // o Windows não diferencia maiúsculas
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.IsNewRow) continue;
            string titulo = r.Cells[0].Value?.ToString()?.Trim() ?? "";
            string nome = Limpar(titulo);
            double? ini = Segundos(r.Cells[1].Value?.ToString()), fim = Segundos(r.Cells[2].Value?.ToString());
            if (nome == "" || ini == null || fim == null || fim <= ini)
            {
                grid.CurrentCell = r.Cells[nome == "" ? 0 : ini == null ? 1 : 2];
                MessageBox.Show($"Linha {r.Index + 1}: preencha nome, início e fim (fim depois do início).");
                return;
            }
            if (!nomes.Add(nome))
            {
                grid.CurrentCell = r.Cells[0];
                MessageBox.Show($"Linha {r.Index + 1}: já existe um corte chamado \"{nome}\". Use nomes diferentes para um não sobrescrever o outro.");
                return;
            }
            cortes.Add((r, nome, titulo, ini.Value, fim.Value));
        }
        if (url.Text.Trim() == "" || cortes.Count == 0)
        {
            MessageBox.Show("Informe a URL e pelo menos um corte.");
            return;
        }
        try { Directory.CreateDirectory(pasta.Text); }
        catch (Exception ex) { MessageBox.Show($"Não foi possível usar a pasta \"{pasta.Text}\": {ex.Message}"); return; }

        rodando = true; cancelado = false;
        iniciar.Text = "CANCELAR";
        grid.ReadOnly = true;
        grid.AllowUserToDeleteRows = false; // ReadOnly não impede o Delete
        foreach (var c in edicao) c.Enabled = false; // URL/pasta/miniatura são lidas a cada corte
        foreach (var c in cortes) c.linha.Cells[3].Value = "Na fila";
        int erros = 0;
        try
        {
            int rc;
            for (int i = 0; i < cortes.Count && !cancelado; i++)
            {
                var (linha, nome, titulo, ini, fim) = cortes[i];
                string etapa = $"Baixando corte {i + 1}/{cortes.Count}: {nome}";
                Progresso(etapa, (double)i / cortes.Count);
                SetStatus(linha, "Baixando...");
                // Com miniatura, cada corte ganha sua pasta: <nome>\<nome>.mp4 + <nome>.png
                string destino = miniatura.Checked ? Directory.CreateDirectory(Path.Combine(pasta.Text, nome)).FullName : pasta.Text;
                // Baixa só o trecho. --force-keyframes-at-cuts recodifica (H.264/AAC) para o corte ser exato;
                // "-progress pipe:1" faz o ffmpeg interno do yt-dlp mandar o progresso pro nosso stdout.
                rc = await Rodar("yt-dlp", ["-f", "bv*+ba/b", "--merge-output-format", "mp4",
                    "--download-sections", $"*{Inv(ini)}-{Inv(fim)}", "--force-keyframes-at-cuts", "--force-overwrites",
                    "--downloader-args", "ffmpeg_o:-crf 20 -preset veryfast -b:a 192k -progress pipe:1 -nostats -loglevel error",
                    "-o", Path.Combine(destino, nome.Replace("%", "%%") + ".%(ext)s"),
                    "--quiet", "--no-warnings", "--encoding", "utf-8", url.Text.Trim()], l =>
                {
                    if (l.StartsWith("out_time_us=") && long.TryParse(l[12..], out var us) && us > 0)
                    {
                        double pct = Math.Min(1, us / 1e6 / (fim - ini));
                        SetStatus(linha, $"Baixando {pct:P0}");
                        Progresso(etapa, (i + pct) / cortes.Count);
                    }
                    else if (!l.Contains('=')) Log(l); // demais linhas do -progress são ruído
                });
                if (cancelado)
                {
                    SetStatus(linha, "Cancelado");
                    foreach (var ext in new[] { ".mp4", ".mp4.part" }) await Apagar(Path.Combine(destino, nome + ext)); // corte pela metade
                    break;
                }
                if (rc != 0) { erros++; SetStatus(linha, "✗ Erro no download"); Log($"Erro ao baixar \"{nome}\"."); continue; }
                if (miniatura.Checked)
                {
                    SetStatus(linha, "Gerando miniatura...");
                    try { await Task.Run(() => Miniatura.Gerar(titulo, Path.Combine(destino, nome + ".png"))); }
                    catch (Exception ex) { erros++; SetStatus(linha, "✗ Erro na miniatura"); Log($"Erro na miniatura de \"{nome}\": {ex.Message}"); continue; }
                }
                SetStatus(linha, "✓ Pronto");
            }
            if (cancelado) return;
            Progresso(erros == 0 ? $"✓ Pronto! {cortes.Count} corte(s) salvos em {Path.GetFileName(Path.TrimEndingDirectorySeparator(pasta.Text))}" : $"Terminado com {erros} erro(s). Veja o log.", 1);
            Process.Start("explorer.exe", pasta.Text);
        }
        catch (Exception ex)
        {
            Progresso("Erro: " + ex.Message, 0);
            Log("Erro: " + ex.Message + " (yt-dlp e ffmpeg estão instalados? winget install yt-dlp ffmpeg)");
        }
        finally
        {
            if (cancelado)
            {
                Progresso("Cancelado.", 0);
                foreach (var c in cortes) if (c.linha.Cells[3].Value is "Na fila") c.linha.Cells[3].Value = "Cancelado";
            }
            rodando = false; atual = null;
            if (!IsDisposed) // a janela pode ter sido fechada no meio
            {
                iniciar.Text = "BAIXAR CORTES";
                foreach (var c in edicao) c.Enabled = true;
                grid.ReadOnly = false;
                grid.AllowUserToDeleteRows = true;
                grid.Columns[3].ReadOnly = true; // grid.ReadOnly = false zera o ReadOnly das colunas
            }
        }
    }

    static string Inv(double s) => s.ToString(CultureInfo.InvariantCulture);

    /// stdout vai linha a linha para `linha`; stderr vai para o log.
    Task<int> Rodar(string exe, string[] args, Action<string> linha)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };
        var fim = new TaskCompletionSource<int>();
        p.OutputDataReceived += (_, e) => { if (e.Data is { } d) linha(d); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is { } d) Log(d); };
        p.Exited += (_, _) => { p.WaitForExit(); fim.TrySetResult(p.ExitCode); p.Dispose(); };
        p.Start();
        atual = p;
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        return fim.Task;
    }

    void Progresso(string texto, double frac) => NaTela(() =>
    {
        status.Text = texto;
        status.ForeColor = texto.StartsWith('✓') ? Ouro : texto.StartsWith("Erro") || texto.Contains("erro(s)") || texto.StartsWith("Falha") ? Erro : Texto;
        barra.Valor = frac;
    });

    void SetStatus(DataGridViewRow r, string s) => NaTela(() => r.Cells[3].Value = s);

    void Log(string s) => NaTela(() => log.AppendText(s + Environment.NewLine));

    // Saída dos processos chega em outra thread e pode chegar depois de a janela fechar.
    void NaTela(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { } // handle destruído entre o teste e a chamada
    }

    void Matar()
    {
        try { atual?.Kill(true); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { } // já terminou
    }

    // Após o Kill, o ffmpeg (filho do yt-dlp) pode segurar o arquivo por alguns instantes.
    static async Task Apagar(string arquivo)
    {
        for (int i = 0; i < 10; i++)
        {
            try { File.Delete(arquivo); return; }
            catch (IOException) { await Task.Delay(300); }
        }
    }

    static string Limpar(string? nome)
    {
        // O Windows apaga pontos/espaços no fim e reserva CON, NUL, COM1... (mesmo com extensão).
        var n = string.Concat((nome ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim().TrimEnd('.', ' ');
        return Reservado.IsMatch(n) ? "_" + n : n;
    }

    static readonly Regex Reservado = new(@"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(\.|$)", RegexOptions.IgnoreCase);
}

/// Barra de progresso fina em ouro; a ProgressBar nativa ignora cores com estilos visuais ligados.
class Barra : Control
{
    double valor;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Valor { get => valor; set { valor = Math.Clamp(value, 0, 1); Invalidate(); } }
    public Barra() => DoubleBuffered = true;
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var b = new SolidBrush(ForeColor);
        e.Graphics.FillRectangle(b, 0, 0, (float)(Width * valor), Height);
    }
}

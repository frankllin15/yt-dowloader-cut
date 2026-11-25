using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

// Porte de tom-de-louvor/gerar_miniatura.py: miniatura 1280x720 do canal Tom de Louvor.
static class Miniatura
{
    const int W = 1280, H = 720, MARGEM = 90;
    static readonly Color VerdeFundo = Color.FromArgb(5, 36, 25), VerdeLuz = Color.FromArgb(16, 92, 62),
        Ouro = Color.FromArgb(214, 180, 106);
    static readonly PrivateFontCollection Fontes = CarregarFontes();
    // MeasureTrailingSpaces: sem isso um " " sozinho mede 0 no rótulo espaçado.
    static readonly StringFormat Tipo = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces };

    static PrivateFontCollection CarregarFontes()
    {
        var c = new PrivateFontCollection();
        foreach (var nome in new[] { "Montserrat.ttf", "GreatVibes-Regular.ttf" })
        {
            var bytes = Recurso(nome);
            // A memória precisa viver enquanto a coleção existir (o processo todo).
            var p = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            c.AddMemoryFont(p, bytes.Length);
        }
        return c;
    }

    internal static byte[] Recurso(string nome)
    {
        using var s = typeof(Miniatura).Assembly.GetManifestResourceStream(nome)!;
        var bytes = new byte[s.Length];
        s.ReadExactly(bytes);
        return bytes;
    }

    // O GDI+ expõe cada peso da fonte variável como família própria ("Montserrat ExtraBold").
    internal static Font Fonte(string familia, float tam, GraphicsUnit unidade = GraphicsUnit.Pixel) =>
        new(Familias.GetOrAdd(familia, f => new FontFamily(f, Fontes)), tam, FontStyle.Regular, unidade);

    // Uma FontFamily por nome, viva o processo todo (antes vazava uma por fonte criada).
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, FontFamily> Familias = new();

    static Font Sans(float tam, string peso = "ExtraBold") => Fonte("Montserrat " + peso, tam);

    public static void Gerar(string musica, string saida)
    {
        string? rotulo = null;
        string titulo = musica.Trim();
        int i = titulo.IndexOf(" - ");
        if (i >= 0) { rotulo = titulo[..i].Trim(); titulo = titulo[(i + 3)..].Trim(); }

        using var img = Fundo();
        using var g = Graphics.FromImage(img);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Marca d'água: cruz grande, translúcida e cortada pela borda direita.
        using (var b = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) Cruz(g, 1080, -40, 1.75f, b, false);

        // Assinatura do canal (topo esquerdo)
        Cruz(g, MARGEM + 17, 64, 0.11f, Brushes.White, true);
        using (var f = Fonte("Great Vibes", 46)) g.DrawString("Tom de Louvor", f, Brushes.White, MARGEM + 50, 66, Tipo);

        // Bloco de título, centralizado verticalmente
        var (fonte, linhas, tam) = Ajustar(g, titulo.ToUpperInvariant(), 820, 330);
        float passo = tam * 1.08f, altRotulo = rotulo != null ? 56 : 0;
        float y = (H - (altRotulo + linhas.Count * passo + 40)) / 2 + 30;

        using var ouro = new SolidBrush(Ouro);
        if (rotulo != null)
        {
            using var fr = Sans(30, "SemiBold");
            float x = MARGEM;
            foreach (char ch in rotulo.ToUpperInvariant())
            {
                g.DrawString(ch.ToString(), fr, ouro, x, y, Tipo);
                x += g.MeasureString(ch.ToString(), fr, PointF.Empty, Tipo).Width + 6;
            }
            y += altRotulo;
        }
        foreach (var l in linhas) { g.DrawString(l, fonte, Brushes.White, MARGEM - 3, y, Tipo); y += passo; }
        fonte.Dispose();
        g.FillRectangle(ouro, MARGEM, y + 18, 90, 6);

        img.Save(saida, ImageFormat.Png);
    }

    static void Cruz(Graphics g, float cx, float topo, float s, Brush cor, bool telhado)
    {
        g.FillRectangle(cor, cx - 20 * s, topo, 40 * s, 400 * s);
        g.FillRectangle(cor, cx - 85 * s, topo + 65 * s, 170 * s, 40 * s);
        if (telhado)
            g.FillPolygon(cor, new PointF[] { new(cx, topo + 360 * s), new(cx + 190 * s, topo + 480 * s), new(cx - 190 * s, topo + 480 * s) });
    }

    static List<string> Quebrar(Graphics g, string texto, Font f, float max)
    {
        var linhas = new List<string>();
        string atual = "";
        foreach (var p in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string teste = (atual + " " + p).Trim();
            if (atual != "" && Largura(g, teste, f) > max) { linhas.Add(atual); atual = p; }
            else atual = teste;
        }
        if (atual != "") linhas.Add(atual);
        return linhas;
    }

    static float Largura(Graphics g, string s, Font f) => g.MeasureString(s, f, PointF.Empty, Tipo).Width;

    static (Font, List<string>, int) Ajustar(Graphics g, string texto, float largura, float altura, int tamMax = 118, int tamMin = 48)
    {
        for (int tam = tamMax; tam >= tamMin; tam -= 2)
        {
            var f = Sans(tam);
            var linhas = Quebrar(g, texto, f, largura);
            if (linhas.Count <= 4 && linhas.Count * tam * 1.08 <= altura && linhas.All(l => Largura(g, l, f) <= largura))
                return (f, linhas, tam);
            f.Dispose();
        }
        var fm = Sans(tamMin);
        return (fm, Quebrar(g, texto, fm, largura), tamMin);
    }

    /// Verde profundo com luz suave à direita, vinheta e leve granulado.
    static Bitmap Fundo()
    {
        var luz = Mascara(g => g.FillEllipse(Brushes.White, W * 0.45f, -H * 0.4f, W * 0.9f, H * 1.5f), 200, 200);
        var vinheta = Mascara(g =>
        {
            using var b = new SolidBrush(Color.FromArgb(110, 110, 110));
            g.FillRectangle(b, 0, 0, W, H);
            g.FillEllipse(Brushes.Black, -W * 0.2f, -H * 0.3f, W * 1.4f, H * 1.6f);
        }, 120, 255);

        var px = new byte[W * H * 4];
        var rnd = new Random(7);
        for (int k = 0; k < W * H; k++)
        {
            float r = VerdeFundo.R, gr = VerdeFundo.G, b = VerdeFundo.B, a = luz[k];
            r += (VerdeLuz.R - r) * a; gr += (VerdeLuz.G - gr) * a; b += (VerdeLuz.B - b) * a;
            a = vinheta[k];
            r -= r * a; gr -= gr * a; b -= b * a;
            // shortcut: granulado sem o blur de 0.6px do original, imperceptível em 1280x720.
            if (rnd.Next(256) > 128) { a = 14 / 255f; r += (255 - r) * a; gr += (255 - gr) * a; b += (255 - b) * a; }
            px[k * 4] = (byte)b; px[k * 4 + 1] = (byte)gr; px[k * 4 + 2] = (byte)r; px[k * 4 + 3] = 255;
        }
        var img = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        var d = img.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, img.PixelFormat);
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        img.UnlockBits(d);
        return img;
    }

    /// Desenha em 1/8 da resolução, aplica blur gaussiano (3 box blurs) e amplia: 0..1 por pixel.
    /// `escala` é a intensidade máxima (200 = fill=200 do Pillow); o desenho usa branco = 255.
    static float[] Mascara(Action<Graphics> desenhar, float sigma, float escala)
    {
        const int S = 8, w = W / S, h = H / S;
        var m = new float[w * h];
        using (var bmp = new Bitmap(w, h))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(1f / S, 1f / S);
                desenhar(g);
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = bmp.GetPixel(x, y).R / 255f * escala / 255f;
        }
        int raio = (int)Math.Round(sigma / S); // 3 passadas de raio r ≈ gaussiana com sigma ≈ r
        for (int p = 0; p < 3; p++) { m = Box(m, w, h, raio, true); m = Box(m, w, h, raio, false); }

        var full = new float[W * H];
        for (int y = 0; y < H; y++)
        {
            float fy = Math.Clamp((y + 0.5f) / S - 0.5f, 0, h - 1); int y0 = (int)fy, y1 = Math.Min(y0 + 1, h - 1); float ty = fy - y0;
            for (int x = 0; x < W; x++)
            {
                float fx = Math.Clamp((x + 0.5f) / S - 0.5f, 0, w - 1); int x0 = (int)fx, x1 = Math.Min(x0 + 1, w - 1); float tx = fx - x0;
                float top = m[y0 * w + x0] + (m[y0 * w + x1] - m[y0 * w + x0]) * tx;
                float bot = m[y1 * w + x0] + (m[y1 * w + x1] - m[y1 * w + x0]) * tx;
                full[y * W + x] = top + (bot - top) * ty;
            }
        }
        return full;
    }

    static float[] Box(float[] src, int w, int h, int r, bool horizontal)
    {
        var dst = new float[src.Length];
        int n = horizontal ? w : h, linhas = horizontal ? h : w;
        for (int l = 0; l < linhas; l++)
            for (int i = 0; i < n; i++)
            {
                float soma = 0;
                for (int k = -r; k <= r; k++)
                {
                    int j = Math.Clamp(i + k, 0, n - 1); // borda estendida, como o Pillow
                    soma += horizontal ? src[l * w + j] : src[j * w + l];
                }
                dst[horizontal ? l * w + i : i * w + l] = soma / (2 * r + 1);
            }
        return dst;
    }
}

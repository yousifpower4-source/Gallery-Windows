using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Gallery;

public class EdState
{
    public int Rot; public bool FlipH, FlipV; public double Angle;
    public double Ex, B, Hl, Sh, C, Bp, Sat, Vib, Warm, Tint, Bw, Sharp, Vig;
    public string Filter = "Original"; public double Inten = 100;
    public Rect? Crop; public string? AspKey; public double Ratio; public bool Swapped;
    public bool EnLight = true, EnColor = true, EnBw = true, EnSharp = true, EnVig = true;
    public double E(double v, bool on) => on ? v : 0;
    public double eEx => E(Ex, EnLight); public double eB => E(B, EnLight); public double eHl => E(Hl, EnLight); public double eSh => E(Sh, EnLight); public double eC => E(C, EnLight); public double eBp => E(Bp, EnLight);
    public double eSat => E(Sat, EnColor); public double eVib => E(Vib, EnColor); public double eWarm => E(Warm, EnColor); public double eTint => E(Tint, EnColor);
    public double eBw => E(Bw, EnBw); public double eSharp => E(Sharp, EnSharp); public double eVig => E(Vig, EnVig);
    public bool Dirty => Rot != 0 || FlipH || FlipV || Angle != 0 || Ex != 0 || B != 0 || Hl != 0 || Sh != 0 || C != 0 || Bp != 0 || Sat != 0 || Vib != 0 || Warm != 0 || Tint != 0 || Bw != 0 || Sharp != 0 || Vig != 0 || Filter != "Original" || Crop != null;
    public EdState Clone() => (EdState)MemberwiseClone();
}

public static class EdRender
{
    public static readonly Dictionary<string, (string fn, double a, double b)[]> Presets = new()
    {
        ["Original"] = Array.Empty<(string, double, double)>(),
        ["Vivid"] = new[] { ("saturate", 1.0, 1.45), ("contrast", 1.0, 1.1) },
        ["Vivid Warm"] = new[] { ("saturate", 1.0, 1.4), ("sepia", 0.0, 0.22) },
        ["Vivid Cool"] = new[] { ("saturate", 1.0, 1.4), ("hue-rotate", 0.0, 14.0) },
        ["Dramatic"] = new[] { ("contrast", 1.0, 1.4), ("saturate", 1.0, 0.8), ("brightness", 1.0, 0.9) },
        ["Dramatic Warm"] = new[] { ("contrast", 1.0, 1.4), ("saturate", 1.0, 0.85), ("brightness", 1.0, 0.92), ("sepia", 0.0, 0.25) },
        ["Dramatic Cool"] = new[] { ("contrast", 1.0, 1.4), ("saturate", 1.0, 0.85), ("brightness", 1.0, 0.92), ("hue-rotate", 0.0, 14.0) },
        ["Mono"] = new[] { ("grayscale", 0.0, 1.0) },
        ["Silvertone"] = new[] { ("grayscale", 0.0, 1.0), ("contrast", 1.0, 1.1), ("brightness", 1.0, 1.06) },
        ["Noir"] = new[] { ("grayscale", 0.0, 1.0), ("contrast", 1.0, 1.45), ("brightness", 1.0, 0.85) },
        ["Fade"] = new[] { ("contrast", 1.0, 0.85), ("brightness", 1.0, 1.1), ("saturate", 1.0, 0.8) },
        ["Chrome"] = new[] { ("contrast", 1.0, 1.2), ("saturate", 1.0, 1.3), ("brightness", 1.0, 1.05) },
        ["Instant"] = new[] { ("sepia", 0.0, 0.25), ("contrast", 1.0, 0.9), ("brightness", 1.0, 1.1), ("saturate", 1.0, 1.3), ("hue-rotate", 0.0, -8.0) },
    };

    // affine colour transform: out = M*rgb + o (values 0..1)
    struct Aff { public double[] M; public double[] O; public static Aff Id() => new() { M = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, O = new double[3] }; }
    static Aff Mul(Aff a, Aff b) // apply a first, then b
    {
        var r = new Aff { M = new double[9], O = new double[3] };
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++) r.M[i * 3 + j] = b.M[i * 3] * a.M[j] + b.M[i * 3 + 1] * a.M[3 + j] + b.M[i * 3 + 2] * a.M[6 + j];
            r.O[i] = b.M[i * 3] * a.O[0] + b.M[i * 3 + 1] * a.O[1] + b.M[i * 3 + 2] * a.O[2] + b.O[i];
        }
        return r;
    }
    static Aff F(string fn, double v)
    {
        var a = Aff.Id();
        switch (fn)
        {
            case "brightness": a.M = new[] { v, 0, 0, 0, v, 0, 0, 0, v }; break;
            case "contrast": a.M = new[] { v, 0, 0, 0, v, 0, 0, 0, v }; a.O = new[] { .5 - .5 * v, .5 - .5 * v, .5 - .5 * v }; break;
            case "saturate": a.M = new[] { .213 + .787 * v, .715 - .715 * v, .072 - .072 * v, .213 - .213 * v, .715 + .285 * v, .072 - .072 * v, .213 - .213 * v, .715 - .715 * v, .072 + .928 * v }; break;
            case "grayscale": { double t = 1 - Math.Clamp(v, 0, 1); a.M = new[] { .2126 + .7874 * t, .7152 - .7152 * t, .0722 - .0722 * t, .2126 - .2126 * t, .7152 + .2848 * t, .0722 - .0722 * t, .2126 - .2126 * t, .7152 - .7152 * t, .0722 + .9278 * t }; break; }
            case "sepia": { double t = 1 - Math.Clamp(v, 0, 1); a.M = new[] { .393 + .607 * t, .769 - .769 * t, .189 - .189 * t, .349 - .349 * t, .686 + .314 * t, .168 - .168 * t, .272 - .272 * t, .534 - .534 * t, .131 + .869 * t }; break; }
            case "hue-rotate": { double r = v * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r); a.M = new[] { .213 + c * .787 - s * .213, .715 - c * .715 - s * .715, .072 - c * .072 + s * .928, .213 - c * .213 + s * .143, .715 + c * .285 + s * .140, .072 - c * .072 - s * .283, .213 - c * .213 - s * .787, .715 - c * .715 + s * .715, .072 + c * .928 + s * .072 }; break; }
        }
        return a;
    }
    static Aff Chain(EdState s)
    {
        var a = F("brightness", (1 + s.eB / 200) * Math.Pow(2, s.eEx / 100));
        a = Mul(a, F("contrast", 1 + s.eC / 200)); a = Mul(a, F("saturate", 1 + s.eSat / 100));
        if (s.eBw != 0) a = Mul(a, F("grayscale", s.eBw / 100));
        double t = s.Inten / 100;
        foreach (var (fn, p0, p1) in Presets.GetValueOrDefault(s.Filter) ?? Array.Empty<(string, double, double)>()) a = Mul(a, F(fn, p0 + (p1 - p0) * t));
        return a;
    }

    public static (int w, int h) Dims(EdState s, int sw, int sh) => s.Rot % 180 != 0 ? (sh, sw) : (sw, sh);

    // src: BGRA premultiplied-ignored opaque
    public static byte[] Render(EdState s, byte[] src, int sw, int sh, bool compare)
    {
        var (w, h) = Dims(s, sw, sh);
        var dst = new byte[w * h * 4];
        double th = compare ? 0 : s.Angle * Math.PI / 180, rot = s.Rot * Math.PI / 180 + th;
        double k = 1; if (!compare && s.Angle != 0) k = Math.Cos(Math.Abs(th)) + Math.Sin(Math.Abs(th)) * Math.Max((double)w / h, (double)h / w);
        double cs = Math.Cos(-rot), sn = Math.Sin(-rot); double fx = !compare && s.FlipH ? -1 : 1, fy = !compare && s.FlipV ? -1 : 1;
        double cx = w / 2.0, cy = h / 2.0, scx = sw / 2.0, scy = sh / 2.0;
        var aff = compare ? Aff.Id() : Chain(s); var M = aff.M; var O = aff.O;
        bool ident = compare || (M[0] == 1 && M[1] == 0 && M[2] == 0 && M[3] == 0 && M[4] == 1 && M[5] == 0 && M[6] == 0 && M[7] == 0 && M[8] == 1 && O[0] == 0 && O[1] == 0 && O[2] == 0);
        double wm = compare ? 0 : s.eWarm / 200, vg = compare ? 0 : s.eVig / 100 * 0.7;
        double wr = wm > 0 ? 1 : 0, wg = wm > 0 ? 140 / 255.0 : 110 / 255.0, wb = wm > 0 ? 0 : 1; double wa = Math.Abs(wm);
        double r0 = Math.Min(w, h) * 0.3, r1 = Math.Sqrt((double)w * w + (double)h * h) / 2;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x + .5 - cx, dy = y + .5 - cy;
                double px = (cs * dx - sn * dy) / k, py = (sn * dx + cs * dy) / k; px *= fx; py *= fy;
                double u = px + scx - .5, v = py + scy - .5;
                int o = (y * w + x) * 4;
                if (u < -1 || v < -1 || u > sw || v > sh) { dst[o + 3] = 255; continue; }
                int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v); double ax = u - x0, ay = v - y0;
                int xa = Math.Clamp(x0, 0, sw - 1), xb = Math.Clamp(x0 + 1, 0, sw - 1), ya = Math.Clamp(y0, 0, sh - 1), yb = Math.Clamp(y0 + 1, 0, sh - 1);
                int i00 = (ya * sw + xa) * 4, i10 = (ya * sw + xb) * 4, i01 = (yb * sw + xa) * 4, i11 = (yb * sw + xb) * 4;
                double B = 0, G = 0, R = 0;
                for (int c = 0; c < 3; c++)
                {
                    double val = (src[i00 + c] * (1 - ax) + src[i10 + c] * ax) * (1 - ay) + (src[i01 + c] * (1 - ax) + src[i11 + c] * ax) * ay;
                    if (c == 0) B = val; else if (c == 1) G = val; else R = val;
                }
                double r = R / 255, g = G / 255, b = B / 255;
                if (!ident)
                {
                    double nr = M[0] * r + M[1] * g + M[2] * b + O[0], ng = M[3] * r + M[4] * g + M[5] * b + O[1], nb = M[6] * r + M[7] * g + M[8] * b + O[2];
                    r = Math.Clamp(nr, 0, 1); g = Math.Clamp(ng, 0, 1); b = Math.Clamp(nb, 0, 1);
                }
                if (wa > 0) { r += (SoftLight(r, wr) - r) * wa; g += (SoftLight(g, wg) - g) * wa; b += (SoftLight(b, wb) - b) * wa; }
                if (vg > 0) { double rr = Math.Sqrt(dx * dx + dy * dy), t = Math.Clamp((rr - r0) / (r1 - r0), 0, 1), m = 1 - vg * t; r *= m; g *= m; b *= m; }
                dst[o] = (byte)Math.Clamp(b * 255 + .5, 0, 255); dst[o + 1] = (byte)Math.Clamp(g * 255 + .5, 0, 255); dst[o + 2] = (byte)Math.Clamp(r * 255 + .5, 0, 255); dst[o + 3] = 255;
            }
        });
        if (!compare) PixelPass(s, dst, w, h);
        return dst;
    }
    static double SoftLight(double cb, double cs)
    {
        if (cs <= .5) return cb - (1 - 2 * cs) * cb * (1 - cb);
        double d = cb <= .25 ? ((16 * cb - 12) * cb + 4) * cb : Math.Sqrt(cb);
        return cb + (2 * cs - 1) * (d - cb);
    }
    static void PixelPass(EdState s, byte[] d, int w, int h)
    {
        double hl = s.eHl, sh = s.eSh, bp = s.eBp, vib = s.eVib, tint = s.eTint, sharp = s.eSharp;
        if (hl == 0 && sh == 0 && bp == 0 && vib == 0 && tint == 0 && sharp == 0) return;
        byte[]? lut = null;
        if (hl != 0 || sh != 0 || bp != 0)
        {
            lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0, b0 = bp / 100 * 0.25; if (b0 != 0) x = Math.Max(0, (x - b0) / (1 - b0));
                x += sh / 100 * 0.35 * 4 * x * (1 - x) * (1 - x); x += hl / 100 * 0.35 * 4 * x * x * (1 - x);
                lut[i] = (byte)Math.Clamp(Math.Round(x * 255), 0, 255);
            }
        }
        byte[]? bl = sharp != 0 ? BoxBlur(d, w, h, Math.Max(1, (int)Math.Round(w / 1600.0))) : null;
        double sa = sharp / 100 * 1.2, va = vib / 100, ti = tint / 100 * 14;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4; double b = d[i], g = d[i + 1], r = d[i + 2];
                if (lut != null) { r = lut[(int)r]; g = lut[(int)g]; b = lut[(int)b]; }
                if (va != 0) { double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), sat = mx > 0 ? (mx - mn) / mx : 0, am = va * (1 - sat), gr = (r + g + b) / 3; r += (r - gr) * am; g += (g - gr) * am; b += (b - gr) * am; }
                if (ti != 0) { g -= ti; r += ti * .5; b += ti * .5; }
                if (bl != null) { r += sa * (r - bl[i + 2]); g += sa * (g - bl[i + 1]); b += sa * (b - bl[i]); }
                d[i] = (byte)Math.Clamp(b + .5, 0, 255); d[i + 1] = (byte)Math.Clamp(g + .5, 0, 255); d[i + 2] = (byte)Math.Clamp(r + .5, 0, 255);
            }
        });
    }
    static byte[] BoxBlur(byte[] s, int w, int h, int rad)
    {
        var t = new byte[s.Length]; var o = new byte[s.Length];
        Parallel.For(0, h, y => { for (int x = 0; x < w; x++) for (int c = 0; c < 3; c++) { int sum = 0, n = 0; for (int k = -rad; k <= rad; k++) { int xx = x + k; if (xx < 0 || xx >= w) continue; sum += s[(y * w + xx) * 4 + c]; n++; } t[(y * w + x) * 4 + c] = (byte)(sum / n); } });
        Parallel.For(0, h, y => { for (int x = 0; x < w; x++) for (int c = 0; c < 3; c++) { int sum = 0, n = 0; for (int k = -rad; k <= rad; k++) { int yy = y + k; if (yy < 0 || yy >= h) continue; sum += t[(yy * w + x) * 4 + c]; n++; } o[(y * w + x) * 4 + c] = (byte)(sum / n); } });
        return o;
    }
}

public class Editor : Grid
{
    readonly MainWindow main; readonly Photo photo; readonly Action<string?> done;
    readonly EdState s = new();
    byte[] prev = Array.Empty<byte>(); int pw, ph;
    WriteableBitmap? wbmp; readonly Image img = new() { Stretch = Stretch.Fill };
    readonly Canvas cropLayer = new(); readonly Grid stageBox = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Grid stage = new(); string tab = "adjust"; bool compare, pending;
    readonly Dictionary<string, Slider> sliders = new(); readonly Dictionary<string, TextBlock> vals = new();
    readonly StackPanel adjustPanel = new(), filtersPanel = new(), cropPanel = new(); readonly Border revertLink = new();
    readonly List<Button> tabBtns = new(); WrapPanel fgrid = new(); StackPanel intenBox = new(); Slider? intenSlider; bool filtersBuilt;
    readonly Dictionary<string, Border> filterBtns = new(); readonly Dictionary<string, Button> aspBtns = new();
    static SolidColorBrush Br(string c) => new((Color)ColorConverter.ConvertFromString(c));

    public Editor(MainWindow main, Photo photo, Action<string?> done)
    {
        this.main = main; this.photo = photo; this.done = done;
        Background = Br("#161617");
        Resources["Win"] = Br("#1C1C1E"); Resources["Text"] = Br("#F5F5F7"); Resources["Muted"] = Br("#98989D"); Resources["Sep"] = Br("#1AFFFFFF"); Resources["Seg"] = Br("#1AFFFFFF");
        Resources["SegSel"] = Br("#636366"); Resources["Pop"] = Br("#2C2C2E"); Resources["Card"] = Br("#2A2A2C"); Resources["Hover"] = Br("#14FFFFFF"); Resources["Accent"] = Br("#0A84FF");
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) }); RowDefinitions.Add(new RowDefinition());
        BuildTop(); BuildBody();
        Loaded += async (_, _) =>
        {
            Focusable = true; Focus();
            var (bmp, w, h) = await Task.Run(() => Thumbs.DecodeFull(photo.Path, 1600));
            if (bmp == null) { main.Toast("Could not open this image for editing"); done(null); return; }
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0); pw = conv.PixelWidth; ph = conv.PixelHeight;
            prev = new byte[pw * ph * 4]; conv.CopyPixels(prev, pw * 4, 0);
            Draw(); SetTab("adjust"); TestHook();
        };
        SizeChanged += (_, _) => Layout();
        PreviewKeyDown += OnKey; PreviewKeyUp += (_, e) => { if (e.Key == Key.C && compare) { compare = false; Sched(); } };
    }

    void TestHook()
    {
        var t = Environment.GetEnvironmentVariable("GALLERY_TEST"); if (string.IsNullOrEmpty(t)) return;
        foreach (var kv in t.Split(';')) { var p = kv.Split('='); if (p.Length != 2) continue; if (p[0] == "tab") SetTab(p[1]); else if (p[0] == "asp") { s.AspKey = p[1]; s.Ratio = p[1] == "16:9" ? 16.0 / 9 : 1; ApplyAspect(); SyncAsp(); ShowCrop(); } else if (p[0] == "filter") { s.Filter = p[1]; if (filtersBuilt) SyncFilterSel(); intenBox.Visibility = Visibility.Visible; } else if (sliders.TryGetValue(p[0], out var sl)) sl.Value = double.Parse(p[1]); }
        Sched();
    }
    // ---------- UI ----------
    TextBlock T(string t, double size = 13, string? fg = null, FontWeight? w = null) { var x = new TextBlock { Text = t, FontSize = size, VerticalAlignment = VerticalAlignment.Center }; if (fg != null) x.Foreground = Br(fg); else x.SetResourceReference(TextBlock.ForegroundProperty, "Text"); if (w != null) x.FontWeight = w.Value; return x; }
    void BuildTop()
    {
        var top = new Grid { Margin = new Thickness(16, 0, 150, 0) }; top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new ColumnDefinition());
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(top, true);
        var cancel = new Button { Style = (Style)FindResource("TbBtn"), Content = T("Cancel"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4) }; cancel.Click += (_, _) => TryClose(); top.Children.Add(cancel);
        var seg = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Background = Br("#1AFFFFFF") }; var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (k, l) in new[] { ("adjust", "Adjust"), ("filters", "Filters"), ("crop", "Crop") }) { var b = new Button { Style = (Style)FindResource("SegBtn"), Content = l, Tag = k }; b.Click += (_, _) => SetTab(k); tabBtns.Add(b); sp.Children.Add(b); }
        seg.Child = sp; Grid.SetColumn(seg, 1); top.Children.Add(seg);
        var r = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(r, 2);
        var rv = new Button { Style = (Style)FindResource("TbBtn"), Content = T("Revert to Original", 12, "#FFD60A"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 6, 0) }; rv.Click += (_, _) => Revert(); r.Children.Add(rv);
        var cmp = new Button { Style = (Style)FindResource("TbBtn"), ToolTip = "Hold to compare (C)", Content = Gallery.Icon.Make("photos", 18) }; cmp.PreviewMouseLeftButtonDown += (_, _) => { compare = true; Sched(); }; cmp.PreviewMouseLeftButtonUp += (_, _) => { compare = false; Sched(); }; cmp.MouseLeave += (_, _) => { if (compare) { compare = false; Sched(); } }; r.Children.Add(cmp);
        var auto = new Button { Style = (Style)FindResource("TbBtn"), ToolTip = "Auto enhance", Content = Gallery.Icon.Make("edit", 18) }; auto.Click += (_, _) => Auto(); r.Children.Add(auto);
        var dn = new Button { Content = "Done", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(16, 5, 16, 5), Foreground = Br("#1C1C1E"), FontWeight = FontWeights.SemiBold, Background = Br("#FFD60A"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
        dn.Template = Pill(Br("#FFD60A"), 7); dn.Click += async (_, _) => await Save(dn); r.Children.Add(dn);
        top.Children.Add(r); var topBar = new Border { Background = Br("#1C1C1E"), Child = top }; SetRow(topBar, 0); Children.Add(topBar); revertBtn = rv;
    }
    Button? revertBtn;
    static ControlTemplate Pill(Brush bg, double radius)
    {
        var t = new ControlTemplate(typeof(Button)); var b = new FrameworkElementFactory(typeof(Border)); b.SetValue(Border.BackgroundProperty, bg); b.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius)); b.SetValue(Border.PaddingProperty, new Thickness(16, 5, 16, 5));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter)); cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); b.AppendChild(cp); t.VisualTree = b; return t;
    }
    void BuildBody()
    {
        var body = new Grid(); SetRow(body, 1); body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        stage.Margin = new Thickness(0); stage.ClipToBounds = true; stageBox.Children.Add(img); stageBox.Children.Add(cropLayer); stage.Children.Add(stageBox);
        img.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.5, Direction = 270 };
        body.Children.Add(stage);
        var panel = new Border { Background = Br("#2A2A2C"), BorderBrush = Br("#1AFFFFFF"), BorderThickness = new Thickness(1, 0, 0, 0) }; Grid.SetColumn(panel, 1);
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12, 10, 12, 20) }; var host = new StackPanel(); sv.Content = host; panel.Child = sv;
        BuildAdjust(); BuildFilters(); BuildCrop(); host.Children.Add(adjustPanel); host.Children.Add(filtersPanel); host.Children.Add(cropPanel);
        var note = T("Done saves a new copy next to the original (“name (edited)”). The original is never changed.", 11, "#98989D"); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(2, 16, 2, 0); host.Children.Add(note);
        body.Children.Add(panel); Children.Add(body);
        stage.SizeChanged += (_, _) => Layout();
        cropLayer.MouseLeftButtonDown += CropDown; cropLayer.MouseMove += CropMove; cropLayer.MouseLeftButtonUp += CropUp; cropLayer.Background = Brushes.Transparent;
    }
    Slider MakeSlider(string key, string label, bool one, Action<double> set, double def = 0, string unit = "")
    {
        var host = new StackPanel { Margin = new Thickness(0, 5, 0, 5) }; var row = new Grid(); var l = T(label, 12, "#98989D"); var v = T(def.ToString("0") + unit, 12); v.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(l); row.Children.Add(v);
        var sl = new Slider { Minimum = one ? 0 : -100, Maximum = 100, Value = def, Height = 16, Style = (Style)FindResource("EdSlider") };
        if (key == "angle") { sl.Minimum = -45; sl.Maximum = 45; }
        sl.ValueChanged += (_, e) => { double val = Math.Round(e.NewValue); v.Text = (!one && key != "inten" && val > 0 ? "+" : "") + val + unit; set(val); Sched(); UpdRevert(); };
        sl.MouseDoubleClick += (_, _) => sl.Value = def;
        host.Children.Add(row); host.Children.Add(sl); sliders[key] = sl; vals[key] = v; pendingHost = host; return sl;
    }
    FrameworkElement? pendingHost;
    FrameworkElement Slide(string key, string label, bool one, Action<double> set) { MakeSlider(key, label, one, set); return pendingHost!; }
    void BuildAdjust()
    {
        FrameworkElement Section(string title, bool open, Action<bool> en, params FrameworkElement[] rows)
        {
            var wrap = new StackPanel(); var head = new Grid { Height = 34, Cursor = Cursors.Hand, Background = Brushes.Transparent }; var hs = new StackPanel { Orientation = Orientation.Horizontal };
            var chev = Gallery.Icon.Make("chevR", 14, Br("#98989D")); chev.RenderTransformOrigin = new Point(.5, .5); chev.RenderTransform = new RotateTransform(open ? 90 : 0); hs.Children.Add(chev); var tt = T(title, 13, null, FontWeights.SemiBold); tt.Margin = new Thickness(6, 0, 0, 0); hs.Children.Add(tt); head.Children.Add(hs);
            var tg = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), BorderBrush = Br("#FFFFFF"), Background = Br("#FFFFFF"), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Toggle " + title, Cursor = Cursors.Hand }; bool on = true;
            tg.Child = new Ellipse { Width = 6, Height = 6, Fill = Br("#1C1C1E") }; head.Children.Add(tg);
            var body = new StackPanel { Visibility = open ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(2, 0, 2, 8) }; foreach (var r in rows) body.Children.Add(r);
            tg.MouseLeftButtonUp += (_, e) => { on = !on; en(on); tg.Background = on ? Br("#FFFFFF") : Brushes.Transparent; tg.BorderBrush = on ? Br("#FFFFFF") : Br("#98989D"); ((Ellipse)tg.Child).Opacity = on ? 1 : 0; body.Opacity = on ? 1 : .45; Sched(); UpdRevert(); e.Handled = true; };
            head.MouseLeftButtonUp += (_, _) => { bool o = body.Visibility != Visibility.Visible; body.Visibility = o ? Visibility.Visible : Visibility.Collapsed; chev.RenderTransform = new RotateTransform(o ? 90 : 0); };
            wrap.Children.Add(head); wrap.Children.Add(body); var bd = new Border { BorderBrush = Br("#1AFFFFFF"), BorderThickness = new Thickness(0, 0, 0, 1), Child = wrap }; return bd;
        }
        adjustPanel.Children.Add(Section("Light", true, v => s.EnLight = v, Slide("ex", "Exposure", false, v => s.Ex = v), Slide("b", "Brightness", false, v => s.B = v), Slide("hl", "Highlights", false, v => s.Hl = v), Slide("sh", "Shadows", false, v => s.Sh = v), Slide("c", "Contrast", false, v => s.C = v), Slide("bp", "Black Point", false, v => s.Bp = v)));
        adjustPanel.Children.Add(Section("Color", false, v => s.EnColor = v, Slide("sat", "Saturation", false, v => s.Sat = v), Slide("vib", "Vibrance", false, v => s.Vib = v), Slide("warm", "Warmth", false, v => s.Warm = v), Slide("tint", "Tint", false, v => s.Tint = v)));
        adjustPanel.Children.Add(Section("Black & White", false, v => s.EnBw = v, Slide("bw", "Intensity", true, v => s.Bw = v)));
        adjustPanel.Children.Add(Section("Sharpen", false, v => s.EnSharp = v, Slide("sharp", "Amount", true, v => s.Sharp = v)));
        adjustPanel.Children.Add(Section("Vignette", false, v => s.EnVig = v, Slide("vig", "Strength", true, v => s.Vig = v)));
    }
    void BuildFilters()
    {
        fgrid = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; filtersPanel.Children.Add(fgrid);
        MakeSlider("inten", "Intensity", true, v => s.Inten = v, 100, "%"); intenBox = new StackPanel { Visibility = Visibility.Collapsed }; intenBox.Children.Add(pendingHost!); filtersPanel.Children.Add(intenBox);
    }
    void BuildFilterThumbs()
    {
        filtersBuilt = true; double k = 64.0 / Math.Max(pw, ph); int tw = Math.Max(1, (int)(pw * k)), th = Math.Max(1, (int)(ph * k)); var tiny = new byte[tw * th * 4];
        for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) { int sx = Math.Min(pw - 1, (int)(x / k)), sy = Math.Min(ph - 1, (int)(y / k)); Array.Copy(prev, (sy * pw + sx) * 4, tiny, (y * tw + x) * 4, 4); }
        foreach (var name in EdRender.Presets.Keys)
        {
            var st = new EdState { Filter = name, Inten = 100 }; var px = EdRender.Render(st, tiny, tw, th, false);
            var wb = new WriteableBitmap(tw, th, 96, 96, PixelFormats.Bgra32, null); wb.WritePixels(new Int32Rect(0, 0, tw, th), px, tw * 4, 0); wb.Freeze();
            var im = new Image { Source = wb, Stretch = Stretch.UniformToFill, Width = 66, Height = 66 }; var cl = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = im, Width = 66, Height = 66 };
            var lab = T(name, 11, "#98989D"); lab.HorizontalAlignment = HorizontalAlignment.Center; lab.TextTrimming = TextTrimming.CharacterEllipsis; lab.Margin = new Thickness(0, 3, 0, 0);
            var sp = new StackPanel(); sp.Children.Add(cl); sp.Children.Add(lab);
            var b = new Border { Width = 74, Margin = new Thickness(0, 0, 4, 8), Padding = new Thickness(2), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(9), Child = sp, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var nm = name; b.MouseLeftButtonUp += (_, _) => { s.Filter = nm; SyncFilterSel(); intenBox.Visibility = nm == "Original" ? Visibility.Collapsed : Visibility.Visible; Sched(); UpdRevert(); };
            filterBtns[name] = b; fgrid.Children.Add(b);
        }
        SyncFilterSel();
    }
    void SyncFilterSel() { foreach (var (n, b) in filterBtns) { b.BorderBrush = n == s.Filter ? Br("#FFD60A") : Brushes.Transparent; ((TextBlock)((StackPanel)b.Child).Children[1]).Foreground = n == s.Filter ? Br("#F5F5F7") : Br("#98989D"); } }
    void BuildCrop()
    {
        cropPanel.Children.Add(PH("Aspect")); var pills = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        foreach (var k in new[] { "None", "Freeform", "Original", "Square", "16:9", "4:3", "3:2", "5:4" })
        {
            var b = new Button { Content = k, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(11, 5, 11, 5), Foreground = Br("#F5F5F7"), FontSize = 12, Cursor = Cursors.Hand, Template = Pill(Br("#1AFFFFFF"), 14) }; var kk = k;
            b.Click += (_, _) => { if (kk == "None") { s.Crop = null; s.Ratio = 0; s.AspKey = null; Sched(); } else { s.AspKey = kk; s.Ratio = kk switch { "Freeform" => 0, "Original" => -1, "Square" => 1, "16:9" => 16.0 / 9, "4:3" => 4.0 / 3, "3:2" => 1.5, _ => 1.25 }; ApplyAspect(); } SyncAsp(); ShowCrop(); UpdRevert(); };
            aspBtns[k] = b; pills.Children.Add(b);
        }
        cropPanel.Children.Add(pills);
        var swap = new Button { Content = "Swap orientation", Margin = new Thickness(0, 0, 0, 6), Foreground = Br("#F5F5F7"), FontSize = 12, Template = Pill(Br("#1AFFFFFF"), 8), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
        swap.Click += (_, _) => { if (s.AspKey == "Original") s.Swapped = !s.Swapped; else if (s.Ratio > 0) s.Ratio = 1 / s.Ratio; ApplyAspect(); ShowCrop(); }; cropPanel.Children.Add(swap);
        cropPanel.Children.Add(PH("Orientation")); var ic = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        Button IB(string name, string tip, Action a) { var b = new Button { Style = (Style)FindResource("TbBtn"), Content = Gallery.Icon.Make(name, 18), ToolTip = tip, Width = 34, Margin = new Thickness(0, 0, 6, 0), Background = Br("#1AFFFFFF") }; b.Click += (_, _) => a(); return b; }
        ic.Children.Add(IB("rotL", "Rotate left", () => { s.Rot = ((s.Rot - 90) % 360 + 360) % 360; ApplyAspect(); Draw(); UpdRevert(); }));
        ic.Children.Add(IB("rotR", "Rotate right", () => { s.Rot = (s.Rot + 90) % 360; ApplyAspect(); Draw(); UpdRevert(); }));
        ic.Children.Add(IB("sidebar", "Flip horizontal", () => { s.FlipH = !s.FlipH; Sched(); UpdRevert(); })); ic.Children.Add(IB("album", "Flip vertical", () => { s.FlipV = !s.FlipV; Sched(); UpdRevert(); }));
        cropPanel.Children.Add(ic); cropPanel.Children.Add(PH("Straighten")); MakeSlider("angle", "Angle", false, v => s.Angle = v, 0, "°"); cropPanel.Children.Add(pendingHost!);
    }
    TextBlock PH(string t) { var x = T(t.ToUpperInvariant(), 11, "#98989D", FontWeights.SemiBold); x.Margin = new Thickness(0, 12, 0, 4); return x; }
    void SyncAsp() { foreach (var (k, b) in aspBtns) b.Template = Pill(k == (s.AspKey ?? "None") ? Br("#636366") : Br("#1AFFFFFF"), 14); }
    void SetTab(string t)
    {
        tab = t; foreach (var b in tabBtns) b.Tag = (string)b.Content == "Adjust" && t == "adjust" || (string)b.Content == "Filters" && t == "filters" || (string)b.Content == "Crop" && t == "crop" ? "on" : "off";
        adjustPanel.Visibility = t == "adjust" ? Visibility.Visible : Visibility.Collapsed; filtersPanel.Visibility = t == "filters" ? Visibility.Visible : Visibility.Collapsed; cropPanel.Visibility = t == "crop" ? Visibility.Visible : Visibility.Collapsed;
        if (t == "filters" && !filtersBuilt && prev.Length > 0) BuildFilterThumbs(); SyncAsp(); ShowCrop();
    }
    void UpdRevert() { if (revertBtn != null) revertBtn.Visibility = s.Dirty ? Visibility.Visible : Visibility.Collapsed; }
    void Revert()
    {
        var n = new EdState(); foreach (var f in typeof(EdState).GetFields()) f.SetValue(s, f.GetValue(n));
        foreach (var (k, sl) in sliders) sl.Value = k == "inten" ? 100 : 0;
        SyncFilterSel(); intenBox.Visibility = Visibility.Collapsed; SyncAsp(); Draw(); ShowCrop(); UpdRevert();
    }
    void Auto()
    {
        if (prev.Length == 0) return; var h = new int[256]; double sum = 0; int n = 0;
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { int sx = x * pw / 128, sy = y * ph / 128; int i = (sy * pw + sx) * 4; int l = (int)Math.Round(.299 * prev[i + 2] + .587 * prev[i + 1] + .114 * prev[i]); h[l]++; sum += l; n++; }
        double mean = sum / n; int lo = 0, hi = 255, a = 0; for (int i = 0; i < 256; i++) { a += h[i]; if (a > n * .01) { lo = i; break; } } a = 0; for (int i = 255; i >= 0; i--) { a += h[i]; if (a > n * .01) { hi = i; break; } }
        void Set(string k, double v) { sliders[k].Value = Math.Round(Math.Clamp(v, -100, 100)); }
        Set("c", Math.Min(40, Math.Max(0, (255.0 / Math.Max(60, hi - lo) - 1) * 70))); Set("b", (118 - mean) / 118 * 40); Set("sat", 12); Set("bp", lo > 8 ? Math.Min(25, lo / 3.0) : 0); Sched();
    }

    // ---------- rendering ----------
    void Sched() { if (pending) return; pending = true; Dispatcher.BeginInvoke(() => { pending = false; Draw(); }, System.Windows.Threading.DispatcherPriority.Render); }
    void Draw()
    {
        if (prev.Length == 0) return;
        var (w, h) = EdRender.Dims(s, pw, ph); var px = EdRender.Render(s, prev, pw, ph, compare);
        if (wbmp == null || wbmp.PixelWidth != w || wbmp.PixelHeight != h) { wbmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null); img.Source = wbmp; }
        wbmp.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0); Layout(); UpdRevert();
    }
    void Layout()
    {
        if (prev.Length == 0 || stage.ActualWidth <= 0) return; var (w, h) = EdRender.Dims(s, pw, ph);
        double k = Math.Min((stage.ActualWidth - 80) / w, (stage.ActualHeight - 80) / h); stageBox.Width = Math.Max(10, Math.Round(w * k)); stageBox.Height = Math.Max(10, Math.Round(h * k)); ShowCrop();
    }

    // ---------- crop ----------
    void ApplyAspect()
    {
        var (w, h) = EdRender.Dims(s, pw, ph); double r = s.Ratio; if (s.AspKey == "Original") r = s.Swapped ? (double)h / w : (double)w / h; effRatio = r; if (s.AspKey == null) { Sched(); return; }
        double ia = (double)w / h, cw, ch; if (r == 0) { cw = ch = .8; } else if (r >= ia) { cw = .9; ch = .9 * ia / r; } else { ch = .9; cw = .9 * r / ia; }
        s.Crop = new Rect((1 - cw) / 2, (1 - ch) / 2, cw, ch); Sched();
    }
    double effRatio; string dragMode = ""; Point dragStart; Rect dragC0;
    Rectangle? cropRect; readonly Ellipse[] handles = new Ellipse[4]; readonly Rectangle[] dims = new Rectangle[4];
    void ShowCrop()
    {
        cropLayer.Children.Clear(); cropLayer.IsHitTestVisible = tab == "crop" && s.Crop != null; if (tab != "crop" || s.Crop == null) return;
        double W = stageBox.Width, H = stageBox.Height; var c = s.Crop.Value; double x = c.X * W, y = c.Y * H, w = c.Width * W, h = c.Height * H;
        void D(double l, double t, double ww, double hh) { var r = new Rectangle { Width = Math.Max(0, ww), Height = Math.Max(0, hh), Fill = Br("#8C000000"), IsHitTestVisible = false }; Canvas.SetLeft(r, l); Canvas.SetTop(r, t); cropLayer.Children.Add(r); }
        D(0, 0, W, y); D(0, y + h, W, H - y - h); D(0, y, x, h); D(x + w, y, W - x - w, h);
        cropRect = new Rectangle { Width = w, Height = h, Stroke = Brushes.White, StrokeThickness = 1.5, Fill = Brushes.Transparent, Cursor = Cursors.SizeAll, Tag = "move" }; Canvas.SetLeft(cropRect, x); Canvas.SetTop(cropRect, y); cropLayer.Children.Add(cropRect);
        foreach (var f in new[] { 1 / 3.0, 2 / 3.0 }) { var l1 = new Line { X1 = x + w * f, X2 = x + w * f, Y1 = y, Y2 = y + h, Stroke = Br("#80FFFFFF"), StrokeThickness = 1, IsHitTestVisible = false }; var l2 = new Line { Y1 = y + h * f, Y2 = y + h * f, X1 = x, X2 = x + w, Stroke = Br("#80FFFFFF"), StrokeThickness = 1, IsHitTestVisible = false }; cropLayer.Children.Add(l1); cropLayer.Children.Add(l2); }
        string[] names = { "nw", "ne", "sw", "se" }; double[][] pos = { new[] { x, y }, new[] { x + w, y }, new[] { x, y + h }, new[] { x + w, y + h } };
        for (int i = 0; i < 4; i++) { var e = new Ellipse { Width = 14, Height = 14, Fill = Brushes.White, Stroke = Br("#66000000"), Tag = names[i], Cursor = i == 0 || i == 3 ? Cursors.SizeNWSE : Cursors.SizeNESW }; Canvas.SetLeft(e, pos[i][0] - 7); Canvas.SetTop(e, pos[i][1] - 7); cropLayer.Children.Add(e); }
    }
    void CropDown(object s_, MouseButtonEventArgs e)
    {
        if (s.Crop == null) return; var src = e.OriginalSource as FrameworkElement; var tag = src?.Tag as string; if (tag == null) return;
        dragMode = tag; dragStart = e.GetPosition(cropLayer); dragC0 = s.Crop.Value; cropLayer.CaptureMouse(); e.Handled = true;
    }
    void CropMove(object s_, MouseEventArgs e)
    {
        if (dragMode == "" || s.Crop == null) return; var p = e.GetPosition(cropLayer); double W = stageBox.Width, H = stageBox.Height; double dx = (p.X - dragStart.X) / W, dy = (p.Y - dragStart.Y) / H; var c0 = dragC0; Rect c;
        if (dragMode == "move") c = new Rect(Math.Clamp(c0.X + dx, 0, 1 - c0.Width), Math.Clamp(c0.Y + dy, 0, 1 - c0.Height), c0.Width, c0.Height);
        else
        {
            bool west = dragMode.Contains('w'), north = dragMode.Contains('n'); var (rw, rh) = EdRender.Dims(s, pw, ph);
            double x1 = west ? c0.X + dx : c0.X, x2 = west ? c0.X + c0.Width : c0.X + c0.Width + dx, y1 = north ? c0.Y + dy : c0.Y, y2 = north ? c0.Y + c0.Height : c0.Y + c0.Height + dy;
            x1 = Math.Clamp(x1, 0, 1); x2 = Math.Clamp(x2, 0, 1); y1 = Math.Clamp(y1, 0, 1); y2 = Math.Clamp(y2, 0, 1);
            double w = Math.Max(.05, Math.Abs(x2 - x1)), h = Math.Max(.05, Math.Abs(y2 - y1)); double r = effRatio;
            if (r != 0) { h = w * rw / (rh * r); if (h > 1) { h = 1; w = h * rh * r / rw; } }
            double ax = west ? c0.X + c0.Width : c0.X, ay = north ? c0.Y + c0.Height : c0.Y; double cx = west ? ax - w : ax, cy = north ? ay - h : ay;
            cx = Math.Clamp(cx, 0, 1 - w); cy = Math.Clamp(cy, 0, 1 - h); c = new Rect(cx, cy, w, h);
        }
        s.Crop = c; ShowCrop(); UpdRevert();
    }
    void CropUp(object s_, MouseButtonEventArgs e) { dragMode = ""; cropLayer.ReleaseMouseCapture(); }

    // ---------- save / close ----------
    async Task Save(Button btn)
    {
        if (!s.Dirty) { main.Toast("No changes to save"); return; }
        btn.Content = "Saving…"; btn.IsEnabled = false;
        try
        {
            var snap = s.Clone(); string path = photo.Path; bool png = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            string? outPath = await Task.Run(() =>
            {
                var (bmp, w0, h0) = Thumbs.DecodeFull(path, 100000); if (bmp == null) return null;
                var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0); int fw = conv.PixelWidth, fh = conv.PixelHeight; var src = new byte[fw * fh * 4]; conv.CopyPixels(src, fw * 4, 0);
                var (w, h) = EdRender.Dims(snap, fw, fh); var px = EdRender.Render(snap, src, fw, fh, false); src = null!;
                BitmapSource res = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                if (snap.Crop is Rect c) { var cr = new Int32Rect((int)Math.Round(c.X * w), (int)Math.Round(c.Y * h), Math.Max(1, (int)Math.Round(c.Width * w)), Math.Max(1, (int)Math.Round(c.Height * h))); cr.Width = Math.Min(cr.Width, w - cr.X); cr.Height = Math.Min(cr.Height, h - cr.Y); res = new CroppedBitmap(res, cr); }
                BitmapEncoder enc = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 92 };
                var conv2 = new FormatConvertedBitmap(res, png ? PixelFormats.Bgra32 : PixelFormats.Bgr32, null, 0); enc.Frames.Add(BitmapFrame.Create(conv2));
                string dir = System.IO.Path.GetDirectoryName(path)!, stem = System.IO.Path.GetFileNameWithoutExtension(path), ext = png ? ".png" : ".jpg"; string o = System.IO.Path.Combine(dir, stem + " (edited)" + ext); int n = 2; while (File.Exists(o)) o = System.IO.Path.Combine(dir, stem + " (edited " + n++ + ")" + ext);
                using var fs = new FileStream(o, FileMode.CreateNew); enc.Save(fs); return o;
            });
            if (outPath == null) { main.Toast("Couldn't save the edited photo"); btn.Content = "Done"; btn.IsEnabled = true; return; }
            done(outPath);
        }
        catch { main.Toast("Couldn't save the edited photo"); btn.Content = "Done"; btn.IsEnabled = true; }
    }
    Border? warn;
    void TryClose()
    {
        if (!s.Dirty) { done(null); return; } if (warn != null) return;
        warn = new Border { Background = Br("#2C2C2E"), BorderBrush = Br("#1AFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 56, 0, 0) };
        var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(T("Discard changes? ")); var keep = new Button { Content = "Keep editing", Margin = new Thickness(10, 0, 6, 0), Foreground = Br("#F5F5F7"), Template = Pill(Br("#1AFFFFFF"), 7), Cursor = Cursors.Hand }; var disc = new Button { Content = "Discard", Foreground = Brushes.White, Template = Pill(Br("#FF3B30"), 7), Cursor = Cursors.Hand };
        keep.Click += (_, _) => { Children.Remove(warn); warn = null; }; disc.Click += (_, _) => done(null); sp.Children.Add(keep); sp.Children.Add(disc); warn.Child = sp; SetRow(warn, 0); SetRowSpan(warn, 2); Children.Add(warn);
    }
    void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.Escape) TryClose();
        else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == 0 && !compare) { compare = true; Sched(); }
        else e.Handled = false;
    }
}

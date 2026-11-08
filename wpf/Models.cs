using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gallery;

public class Icon : Control
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(Icon));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Icon));
    public Geometry Data { get => (Geometry)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public static Icon Make(string name, double size, Brush? fg = null)
    {
        var i = new Icon { Data = (Geometry)Application.Current.FindResource("ic_" + name), Width = size, Height = size };
        if (fg != null) i.Foreground = fg; else i.SetResourceReference(ForegroundProperty, "Text");
        return i;
    }
}

public class Photo : INotifyPropertyChanged
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public long Mtime { get; init; }
    public long Size { get; init; }
    public DateTime Date => DateTimeOffset.FromUnixTimeMilliseconds(Mtime).LocalDateTime;
    BitmapSource? thumb; int fav, sel;
    public int Want, ThumbPx; public double Ar; public bool Failed;
    public BitmapSource? Thumb { get => thumb; set { thumb = value; if (value == null) ThumbPx = 0; N(nameof(Thumb)); } }
    public bool IsFav { get => fav == 1; set { fav = value ? 1 : 0; N(nameof(IsFav)); } }
    public bool IsSel { get => sel == 1; set { sel = value ? 1 : 0; N(nameof(IsSel)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    void N(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class WinState
{
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 820;
    public bool Maximized { get; set; }
}

public class Settings
{
    public List<string> Folders { get; set; } = new();
    public List<string> Favs { get; set; } = new();
    public List<string> Hidden { get; set; } = new();
    public Dictionary<string, int> Rot { get; set; } = new();
    public Dictionary<string, List<string>> Albums { get; set; } = new();
    public List<JsonElement> Trashed { get; set; } = new();
    public string Theme { get; set; } = "system";
    public double Zoom { get; set; } = 220;
    public string Mode { get; set; } = "all";
    public WinState? Win { get; set; }

    static readonly JsonSerializerOptions Opt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
    public static string Dir => Environment.GetEnvironmentVariable("GALLERY_DATA") is { Length: > 0 } d ? d : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GalleryWpf");
    static string File_ => System.IO.Path.Combine(Dir, "settings.json");
    public static Settings Load()
    {
        foreach (var f in new[] { File_, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gallery-windows", "settings.json") })
        {
            try { if (File.Exists(f)) { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(f), Opt); if (s != null) { if (f != File_) s.Win = null; return s; } } } catch { }
        }
        return new Settings();
    }
    public void Save() { try { Directory.CreateDirectory(Dir); File.WriteAllText(File_, JsonSerializer.Serialize(this, Opt)); } catch { } }
}

public abstract class RowItem { public abstract FrameworkElement Build(MainWindow w); }
public class HeaderRow : RowItem
{
    public string Text = "", Sub = "";
    public override FrameworkElement Build(MainWindow w)
    {
        var t = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(16, 12, 16, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        t.Inlines.Add(new System.Windows.Documents.Run(Text));
        var r = new System.Windows.Documents.Run("   " + Sub) { FontSize = 12, FontWeight = FontWeights.Normal };
        r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
        t.Inlines.Add(r);
        return t;
    }
}
public class TileRow : RowItem
{
    public Photo[] Items = Array.Empty<Photo>(); public double Size, Gap = 2, Left; public double[]? Widths;
    public override FrameworkElement Build(MainWindow w)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Left, 0, 0, Gap), Height = Size };
        for (int i = 0; i < Items.Length; i++) { var t = w.MakeTile(Items[i], Size, Widths?[i]); t.Margin = new Thickness(0, 0, Gap, 0); sp.Children.Add(t); }
        return sp;
    }
}
public class Card { public string Title = "", Sub = ""; public Photo[] Photos = Array.Empty<Photo>(); public Action? Click; }
public class CardRow : RowItem
{
    public Card[] Items = Array.Empty<Card>(); public double W, H; public bool Year;
    public override FrameworkElement Build(MainWindow w)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 10) };
        foreach (var c in Items) { var v = w.MakeCard(c, W, H, Year); v.Margin = new Thickness(0, 0, 10, 0); sp.Children.Add(v); }
        return sp;
    }
}
public class RowHost : Decorator
{
    public RowHost() { DataContextChanged += (_, _) => Child = (DataContext as RowItem)?.Build(MainWindow.Instance!); }
}

public static class Thumbs
{
    static readonly ConcurrentStack<(Photo p, int px)> stack = new();
    static int workers, decoded;
    public static int Pending => stack.Count + Volatile.Read(ref workers);
    public static int Decoded => Volatile.Read(ref decoded);
    static readonly string CacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalleryWpf", "thumbs");
    static BitmapSource? DecodeCached(Photo p, int px)
    {
        string key;
        try { key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(p.Path + "|" + p.Mtime + "|" + p.Size + "|" + px))); } catch { return Decode(p.Path, px); }
        string f = System.IO.Path.Combine(CacheDir, key + ".jpg");
        try
        {
            if (File.Exists(f))
            {
                var c = new BitmapImage(); c.BeginInit(); c.UriSource = new Uri(f); c.CacheOption = BitmapCacheOption.OnLoad; c.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; c.EndInit(); c.Freeze(); return c;
            }
        }
        catch { }
        var b = Decode(p.Path, px); if (b == null) return null;
        try
        {
            Directory.CreateDirectory(CacheDir);
            var enc = new JpegBitmapEncoder { QualityLevel = 85 }; enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(b, PixelFormats.Bgr32, null, 0)));
            var tmp = f + "." + Environment.CurrentManagedThreadId + ".tmp"; using (var fs = new FileStream(tmp, FileMode.Create)) enc.Save(fs); File.Move(tmp, f, true);
        }
        catch { }
        return b;
    }
    public static void Request(Photo p, int px)
    {
        p.Want++;
        if (p.Thumb != null && p.ThumbPx >= px) return;
        stack.Push((p, px)); Pump();
    }
    public static void Release(Photo p)
    {
        if (--p.Want <= 0) { p.Want = 0; p.Thumb = null; }
    }
    static void Pump()
    {
        while (workers < Math.Clamp(Environment.ProcessorCount / 2, 2, 4) && !stack.IsEmpty)
        {
            Interlocked.Increment(ref workers);
            Task.Run(() =>
            {
                try
                {
                    while (stack.TryPop(out var it))
                    {
                        var p = it.p; int px = it.px;
                        if (p.Want <= 0) continue;
                        if (p.Thumb != null && p.ThumbPx >= px) continue;
                        var bmp = DecodeCached(p, px);
                        if (bmp == null) { p.Failed = true; continue; }
                        if (Interlocked.Increment(ref decoded) % 24 == 0) GC.Collect(1, GCCollectionMode.Forced, false);
                        Application.Current.Dispatcher.BeginInvoke(() => { if (p.Want > 0) { p.Thumb = bmp; p.ThumbPx = px; MainWindow.Instance?.TouchIdle(); } });
                    }
                }
                finally { Interlocked.Decrement(ref workers); }
                if (!stack.IsEmpty) Pump(); // an item may have been pushed just after the last pop
            });
        }
    }

    static bool HasExif(string path)
    {
        var e = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return e is ".jpg" or ".jpeg" or ".jpe" or ".jfif" or ".tif" or ".tiff";
    }
    public static int OrientationOf(BitmapFrame f)
    {
        try
        {
            if (f.Metadata is not BitmapMetadata md) return 1;
            foreach (var q in new[] { "System.Photo.Orientation", "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try { if (md.ContainsQuery(q) && md.GetQuery(q) is { } v) { int o = Convert.ToInt32(v); if (o is >= 1 and <= 8) return o; } } catch { }
            }
        }
        catch { }
        return 1;
    }
    public static int Orientation(string path)
    {
        if (!HasExif(path)) return 1;
        try { using var fs = File.OpenRead(path); return OrientationOf(BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None)); }
        catch { return 1; }
    }
    static BitmapSource Orient(BitmapSource b, int o)
    {
        if (o == 1) return b;
        Transform t = o switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => Transform.Identity
        };
        var r = new TransformedBitmap(b, t); r.Freeze(); return r;
    }

    static readonly SemaphoreSlim heavy = new(1, 1);
    public static BitmapSource? Decode(string path, int px)
    {
        bool gate = false;
        try
        {
            try { var hf = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); if ((long)hf.PixelWidth * hf.PixelHeight > 8_000_000) { heavy.Wait(); gate = true; } } catch { }
            int o = Orientation(path);
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(path);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (px > 0) { if (o >= 5) b.DecodePixelHeight = px; else b.DecodePixelWidth = px; }
            b.EndInit(); b.Freeze();
            return Orient(b, o);
        }
        catch { return null; }
        finally { if (gate) heavy.Release(); }
    }
    public static (BitmapSource? img, int w, int h) DecodeFull(string path, int maxSide)
    {
        bool gate = false;
        try
        {
            int o = Orientation(path);
            var f = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            int w = f.PixelWidth, h = f.PixelHeight; if (o >= 5) (w, h) = (h, w); // oriented size
            if ((long)w * h > 8_000_000) { heavy.Wait(); gate = true; }
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(path);
            b.CacheOption = BitmapCacheOption.OnLoad;
            // limit the longest oriented side to maxSide
            if (w >= h && w > maxSide) { if (o >= 5) b.DecodePixelHeight = maxSide; else b.DecodePixelWidth = maxSide; }
            else if (h > w && h > maxSide) { if (o >= 5) b.DecodePixelWidth = maxSide; else b.DecodePixelHeight = maxSide; }
            b.EndInit(); b.Freeze();
            return (Orient(b, o), w, h);
        }
        catch { return (null, 0, 0); }
        finally { if (gate) heavy.Release(); }
    }
}

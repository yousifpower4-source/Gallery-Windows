using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.VisualBasic.FileIO;

namespace Gallery;

public partial class MainWindow : Window
{
    public static MainWindow? Instance;
    static readonly HashSet<string> NativeExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".apng", ".gif", ".webp", ".bmp", ".dib", ".ico", ".avif", ".svg", ".heic", ".heif", ".tif", ".tiff" };
    static readonly string[] Months = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
    static readonly CultureInfo GB = new("en-GB");

    Settings S = new();
    List<Photo> all = new();
    List<Photo> list = new();
    readonly Dictionary<string, Photo> byPath = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<Photo> sel = new();
    Photo? anchor;
    string view = "lib", mode = "all";
    int? drillY, drillM;
    bool ready, external;
    ScrollViewer? inner;
    double zoom = 220;
    DispatcherTimerLite? rebuildTimer;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        S = Settings.Load();
        zoom = Math.Clamp(S.Zoom, 120, 420); mode = S.Mode is "years" or "months" or "days" or "all" ? S.Mode : "all";
        ZoomSlider.Value = zoom;
        if (S.Folders.Count == 0) S.Folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        foreach (var f in S.Favs) { }
        ApplyTheme(S.Theme);
        RestoreBounds_();
        Loaded += async (_, _) =>
        {
            Rows.ApplyTemplate();
            inner = Rows.Template.FindName("PART_Scroll", Rows) as ScrollViewer;
            double lastVw = 0; inner!.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0 && Math.Abs(inner.ViewportWidth - lastVw) > 0.5) { lastVw = inner.ViewportWidth; RowsRelayout(); } };
            ready = true;
            BuildSide(); UpdateSeg();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => { if (S.Theme == "system") Dispatcher.Invoke(() => ApplyTheme("system")); };
            if (LoadIndex()) { BuildSide(); Rebuild(); }
            await Rescan();
            TestView();
            if (App.StartFile != null) { OnExternalOpen(App.StartFile); if (App.EditFlag && idx >= 0) OpenEditor(lbList[idx]); }
        };
        StateChanged += (_, _) => OnState();
        Closing += (_, _) => SaveBounds();
    }

    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    DispatcherTimerLite? idleTrim;
    public void TouchIdle() { idleTrim ??= new DispatcherTimerLite(this, 2500, () => { try { GC.Collect(2, GCCollectionMode.Forced, true, true); EmptyWorkingSet(Process.GetCurrentProcess().Handle); } catch { } }); idleTrim.Kick(); }
    async void AutoTest()
    {
        string outp = Environment.GetEnvironmentVariable("GALLERY_AUTOTEST")!; var log = new LiveLog(outp);
        try
        {
            log.Add("photos=" + all.Count + " list=" + list.Count);
            var a = list[0]; ToggleFav(a); log.Add("fav " + a.IsFav + " saved=" + S.Favs.Count);
            HidePhotos(new List<Photo> { list[1] }, true); log.Add("after hide list=" + list.Count + " hidden=" + S.Hidden.Count);
            var d = list[2]; string dp = d.Path; await DeletePhotos(new[] { d }); log.Add("err=" + LastDeleteError); log.Add("after delete exists=" + File.Exists(dp) + " list=" + list.Count + " trashed=" + S.Trashed.Count);
            var d2 = list[2]; string dp2 = d2.Path; int tr0 = S.Trashed.Count; await DeletePhotos(new[] { d2 }, true); log.Add("permanent exists=" + File.Exists(dp2) + " trashed delta=" + (S.Trashed.Count - tr0) + " list=" + list.Count);
            SearchTb.Text = "zzzz"; await Task.Delay(300); log.Add("search zzzz -> " + list.Count); SearchTb.Text = ""; await Task.Delay(300); log.Add("search cleared -> " + list.Count);
            SetView("fav"); log.Add("fav view " + list.Count); SetView("hidden"); log.Add("hidden view " + list.Count); SetView("deleted"); log.Add("deleted rows ok"); SetView("lib");
            AddToAlbum("T", new List<Photo> { list[0] }); SetView("a:T"); log.Add("album view " + list.Count); SetView("lib");
            foreach (var m in new[] { "years", "months", "days", "all" }) { mode = m; Rebuild(); log.Add(m + " rows=" + (Rows.ItemsSource as List<RowItem>)?.Count); }
            OpenLb(0); await Task.Delay(500); Step(1); await Task.Delay(300); Rotate(90); log.Add("lb idx=" + idx + " rot=" + LbRot.Angle); CloseLb();
            log.Add("DONE");
        }
        catch (Exception ex) { log.Add("ERR " + ex); }
        Close();
    }
    async void MemTest()
    {
        string outp = Environment.GetEnvironmentVariable("GALLERY_MEMTEST")!; var log = new LiveLog(outp);
        string M() { var p = Process.GetCurrentProcess(); p.Refresh(); return "priv=" + p.PrivateMemorySize64 / 1048576 + "MB ws=" + p.WorkingSet64 / 1048576 + "MB photos=" + all.Count; }
        await Task.Delay(8000); log.Add("idle after start: " + M());
        for (int i = 0; i < 60; i++) { inner?.ScrollToVerticalOffset(i * 900); await Task.Delay(120); }
        log.Add("during/after fast scroll: " + M()); await Task.Delay(6000); log.Add("scrolled, idle 6s: " + M());
        inner?.ScrollToVerticalOffset(0); await Task.Delay(500); external = false; OpenLb(0); await Task.Delay(3000); log.Add("photo open: " + M());
        for (int i = 0; i < 12; i++) { Step(1); await Task.Delay(700); var q = lbList[idx]; log.Add("  step " + i + " " + System.IO.Path.GetFileName(q.Path) + " ar=" + q.Ar.ToString("0.00") + " " + (q.Size / 1048576.0).ToString("0.0") + "MB -> " + M()); } log.Add("after 12 photos in viewer: " + M());
        await Task.Delay(6000); log.Add("viewer idle 6s: " + M()); CloseLb(); await Task.Delay(6000); log.Add("closed viewer, idle 6s: " + M()); log.Add("DONE"); Close();
    }
    async void PerfTest()
    {
        string outp = Environment.GetEnvironmentVariable("GALLERY_PERF")!; var log = new LiveLog(outp); var st = Process.GetCurrentProcess().StartTime;
        double Now() => (DateTime.Now - st).TotalMilliseconds;
        async Task<double> Settle() { int quiet = 0; double t0 = Now(); await Task.Delay(50); while (quiet < 4) { quiet = (Thumbs.Pending == 0) ? quiet + 1 : 0; await Task.Delay(50); if (Now() - t0 > 60000) break; } return Now() - 200; }
        log.Add("first rows on screen at " + (int)firstRowsMs + " ms; window loaded+rescan done at " + (int)Now() + " ms, photos=" + all.Count + " rows=" + (Rows.ItemsSource as List<RowItem>)?.Count);
        log.Add("first screen thumbs settled at " + (int)await Settle() + " ms (decoded " + Thumbs.Decoded + ")");
        double t1 = Now(); for (int i = 0; i < 40; i++) { inner?.ScrollToVerticalOffset(i * 700); await Task.Delay(40); }
        double t2 = Now(); await Settle(); log.Add("fast scroll 40 steps took " + (int)(t2 - t1) + " ms; thumbs settled +" + (int)(Now() - t2 - 200) + " ms after (decoded " + Thumbs.Decoded + ")");
        inner?.ScrollToVerticalOffset(0); await Settle();
        for (int k = 0; k < 4; k++)
        {
            double a0 = Now(); external = false; OpenLb(k * 3); while (true) { await Task.Delay(10); if (lbDecodedTok == lbToken) break; if (Now() - a0 > 20000) break; }
            log.Add("viewer open photo #" + (k * 3) + " full image shown in " + (int)(Now() - a0) + " ms");
        }
        double s0 = Now(); for (int k = 0; k < 6; k++) { double b0 = Now(); Step(1); while (true) { await Task.Delay(5); if (lbDecodedTok == lbToken) break; if (Now() - b0 > 20000) break; } }
        log.Add("6 next-photo steps avg " + (int)((Now() - s0) / 6) + " ms each");
        log.Add("DONE"); Close();
    }
    int lbDecodedTok = -1; double firstRowsMs;
    void TestView()
    {
        if (Environment.GetEnvironmentVariable("GALLERY_PERF") != null) { PerfTest(); return; }
        if (Environment.GetEnvironmentVariable("GALLERY_MEMTEST") != null) { MemTest(); return; }
        if (Environment.GetEnvironmentVariable("GALLERY_AUTOTEST") != null) { AutoTest(); return; }
        var t = Environment.GetEnvironmentVariable("GALLERY_VIEW"); if (string.IsNullOrEmpty(t)) return;
        foreach (var kv in t.Split(';')) { var p = kv.Split('='); if (p.Length != 2) continue; if (p[0] == "theme") { ApplyTheme(p[1]); } else if (p[0] == "mode") { mode = p[1]; Rebuild(); } else if (p[0] == "view") SetView(p[1]); else if (p[0] == "sidebar" && p[1] == "0") { Sidebar.Width = 0; RowsRelayout(); } else if (p[0] == "scroll") inner?.ScrollToVerticalOffset(double.Parse(p[1])); }
    }
    // ---------- window / theme ----------
    void RestoreBounds_()
    {
        var w = S.Win; if (w == null || double.IsNaN(w.X)) { WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        double vx = SystemParameters.VirtualScreenLeft, vy = SystemParameters.VirtualScreenTop, vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        if (w.X + 120 > vx && w.X < vx + vw - 120 && w.Y + 60 > vy && w.Y < vy + vh - 120)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = w.X; Top = w.Y; Width = Math.Max(760, w.Width); Height = Math.Max(520, w.Height); if (w.Maximized) WindowState = WindowState.Maximized; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
    void SaveBounds()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : base.RestoreBounds;
        S.Win = new WinState { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, Maximized = WindowState == WindowState.Maximized };
        S.Save();
    }
    void OnState()
    {
        bool max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(0) : new Thickness(0);
        CapMaxTx.Text = max ? "" : "";
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        src.AddHook(WndProc);
    }
    // Keep a maximized borderless window inside the work area of the monitor it is on.
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x0024) // WM_GETMINMAXINFO
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lp);
            var mon = MonitorFromWindow(hwnd, 2);
            if (mon != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(mon, ref mi))
                {
                    mmi.ptMaxPosition.x = mi.rcWork.left - mi.rcMonitor.left; mmi.ptMaxPosition.y = mi.rcWork.top - mi.rcMonitor.top;
                    mmi.ptMaxSize.x = mi.rcWork.right - mi.rcWork.left; mmi.ptMaxSize.y = mi.rcWork.bottom - mi.rcWork.top;
                    Marshal.StructureToPtr(mmi, lp, true);
                }
            }
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, int f);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    void CapMin_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void CapMax_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CapClose_Click(object s, RoutedEventArgs e) => Close();
    void CapClose_Enter(object s, MouseEventArgs e) { CapCloseTx.Foreground = Brushes.White; ((Button)s).Template = CloseHover(); }
    void CapClose_Leave(object s, MouseEventArgs e) { CapCloseTx.SetResourceReference(TextBlock.ForegroundProperty, "Text"); ((Button)s).ClearValue(TemplateProperty); }
    ControlTemplate CloseHover()
    {
        var t = new ControlTemplate(typeof(Button));
        var b = new FrameworkElementFactory(typeof(Border)); b.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter)); cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        b.AppendChild(cp); t.VisualTree = b; return t;
    }

    static bool SystemLight() { try { return (int)(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) ?? 1) != 0; } catch { return true; } }
    void ApplyTheme(string t)
    {
        S.Theme = t; bool dark = t == "dark" || (t == "system" && !SystemLight());
        void B(string k, string c) => Application.Current.Resources[k] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
        if (dark) { B("Win", "#1E1E1E"); B("Side", "#262628"); B("Text", "#F5F5F7"); B("Muted", "#98989D"); B("Sep", "#1AFFFFFF"); B("Hover", "#14FFFFFF"); B("Seg", "#1AFFFFFF"); B("SegSel", "#636366"); B("Card", "#2C2C2E"); B("Lb", "#000000"); B("Hdr", "#E01E1E1E"); B("Pop", "#2C2C2E"); }
        else { B("Win", "#FFFFFF"); B("Side", "#EFEFF1"); B("Text", "#1D1D1F"); B("Muted", "#6E6E73"); B("Sep", "#1A000000"); B("Hover", "#0F000000"); B("Seg", "#12000000"); B("SegSel", "#FFFFFF"); B("Card", "#F2F2F7"); B("Lb", "#FFFFFF"); B("Hdr", "#E0FFFFFF"); B("Pop", "#FFFFFF"); }
        if (ThLight != null) { ThLight.Tag = t == "light" ? "on" : "light"; ThAuto.Tag = t == "system" ? "on" : "system"; ThDark.Tag = t == "dark" ? "on" : "dark"; }
    }
    void DoDelete(List<Photo> ps) => _ = DeletePhotos(ps);
    void ThemeBtn_Click(object s, RoutedEventArgs e) =>  ThemePop.IsOpen = !ThemePop.IsOpen;
    void Theme_Click(object s, RoutedEventArgs e)
    {
        var b = (Button)s; string t = b == ThLight ? "light" : b == ThDark ? "dark" : "system";
        ApplyTheme(t); S.Save();
    }
    void SideToggle_Click(object s, RoutedEventArgs e)
    {
        bool open = Sidebar.Width > 1; var a = new DoubleAnimation(open ? 0 : 220, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Sidebar.BeginAnimation(WidthProperty, a);
        a.Completed += (_, _) => { Sidebar.BeginAnimation(WidthProperty, null); Sidebar.Width = open ? 0 : 220; if (open) Sidebar.BorderThickness = new Thickness(0); else Sidebar.BorderThickness = new Thickness(0, 0, 1, 0); RowsRelayout(); };
    }

    // ---------- toast ----------
    public void Toast(string m)
    {
        ToastText.Text = m; ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames = { new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(200)), new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(2000)), new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(2250)) }
        });
    }

    // ---------- scanning ----------
    static List<Photo> ScanDir(string dir, bool recursive, int depth = 0)
    {
        var res = new List<Photo>();
        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if (f.Name.StartsWith('.')) continue;
                if (f is DirectoryInfo d) { if (recursive && depth < 6) res.AddRange(ScanDir(d.FullName, true, depth + 1)); }
                else if (f is FileInfo fi && NativeExt.Contains(fi.Extension))
                    res.Add(new Photo { Path = fi.FullName, Name = fi.Name, Mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(), Size = fi.Length });
            }
        }
        catch { }
        return res;
    }
    static string IndexFile => System.IO.Path.Combine(Settings.Dir, "index.json");
    bool LoadIndex()
    {
        try
        {
            if (!File.Exists(IndexFile)) return false;
            var favs = new HashSet<string>(S.Favs, StringComparer.OrdinalIgnoreCase); var folders = S.Folders;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(IndexFile));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string path = e[0].GetString() ?? ""; if (!folders.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase)) || byPath.ContainsKey(path)) continue;
                var p = new Photo { Path = path, Name = System.IO.Path.GetFileName(path), Mtime = e[1].GetInt64(), Size = e[2].GetInt64(), Ar = e.GetArrayLength() > 3 ? e[3].GetDouble() : 0 }; p.IsFav = favs.Contains(path); byPath[path] = p; all.Add(p);
            }
            return all.Count > 0;
        }
        catch { all.Clear(); byPath.Clear(); return false; }
    }
    void SaveIndex()
    {
        var snap = all.ToList();
        Task.Run(() => { try { Directory.CreateDirectory(Settings.Dir); using var fs = File.Create(IndexFile + ".tmp"); using (var w = new Utf8JsonWriter(fs)) { w.WriteStartArray(); foreach (var p in snap) { w.WriteStartArray(); w.WriteStringValue(p.Path); w.WriteNumberValue(p.Mtime); w.WriteNumberValue(p.Size); w.WriteNumberValue(Math.Round(p.Ar, 4)); w.WriteEndArray(); } w.WriteEndArray(); } fs.Close(); File.Move(IndexFile + ".tmp", IndexFile, true); } catch { } });
    }
    async Task Rescan()
    {
        var folders = S.Folders.ToList();
        var found = await Task.Run(() => { var l = new List<Photo>(); foreach (var f in folders) l.AddRange(ScanDir(f, true)); return l; });
        var favs = new HashSet<string>(S.Favs, StringComparer.OrdinalIgnoreCase);
        var old = new Dictionary<string, Photo>(byPath, StringComparer.OrdinalIgnoreCase);
        var nl = new List<Photo>(found.Count); var nb = new Dictionary<string, Photo>(StringComparer.OrdinalIgnoreCase); bool changed = old.Count != found.Count;
        foreach (var f in found)
        {
            if (nb.ContainsKey(f.Path)) continue; Photo p = f;
            if (old.TryGetValue(f.Path, out var o) && o.Mtime == f.Mtime && o.Size == f.Size) p = o; else { changed = true; }
            p.IsFav = favs.Contains(p.Path); nb[p.Path] = p; nl.Add(p);
        }
        if (nl.Count != old.Count) changed = true;
        all = nl; byPath.Clear(); foreach (var kv in nb) byPath[kv.Key] = kv.Value;
        BuildSide(); if (changed || list.Count == 0) Rebuild(true);
        _ = Task.Run(() => { Parallel.ForEach(all.ToList(), new ParallelOptions { MaxDegreeOfParallelism = 3 }, p => { if (p.Ar > 0) return; try { using var fs = File.OpenRead(p.Path); var d = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None); var f = d.Frames[0]; if (f.PixelHeight > 0) { p.Ar = (double)f.PixelWidth / f.PixelHeight; int ori = Thumbs.OrientationOf(f); if (ori >= 5 && ori <= 8) p.Ar = 1 / p.Ar; } } catch { p.Ar = 1; } }); }).ContinueWith(_ => Dispatcher.BeginInvoke(() => { SaveIndex(); if (EffMode == "days") Rebuild(true); }));
        SaveIndex();
    }

    // ---------- sidebar ----------
    TextBlock SideH(string t, bool plus = false)
    {
        var tb = new TextBlock { Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 14, 8, 4) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return tb;
    }
    Button SideRow(string v, string ico, string label, UIElement? lead = null, int? count = null, string? tip = null)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool active = view == v;
        UIElement l = lead ?? Gallery.Icon.Make(ico, 18, active ? Brushes.White : (Brush)FindResource("Accent"));
        g.Children.Add(l);
        var tb = new TextBlock { Text = label, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tb, 1); g.Children.Add(tb);
        if (count != null) { var c = new TextBlock { Text = count.ToString(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = active ? new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)) : (Brush)FindResource("Muted") }; Grid.SetColumn(c, 2); g.Children.Add(c); }
        var b = new Button { Style = (Style)FindResource("RowBtn"), Content = g, Tag = active ? "active" : null, ToolTip = tip, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) => SetView(v);
        return b;
    }
    void BuildSide()
    {
        if (!ready) return;
        var st = SideStack; st.Children.Clear();
        var h = SideH("Library"); h.Margin = new Thickness(8, 0, 8, 4); st.Children.Add(h);
        st.Children.Add(SideRow("lib", "photos", "Library")); st.Children.Add(SideRow("fav", "heart", "Favorites")); st.Children.Add(SideRow("recent", "clock", "Recents"));
        st.Children.Add(SideH("Media Types")); st.Children.Add(SideRow("shots", "shot", "Screenshots")); st.Children.Add(SideRow("anim", "anim", "Animated"));
        var ah = new Grid { Margin = new Thickness(8, 14, 8, 4) };
        var ahT = new TextBlock { Text = "Albums", FontSize = 11, FontWeight = FontWeights.SemiBold }; ahT.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); ah.Children.Add(ahT);
        var plus = new Button { Style = (Style)FindResource("TbBtn"), Width = 18, Height = 18, MinWidth = 18, Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Add Folder", Content = Gallery.Icon.Make("plus", 14, (Brush)FindResource("Muted")) };
        plus.Click += AddFolder_Click; ah.Children.Add(plus); st.Children.Add(ah);
        foreach (var f in S.Folders)
        {
            var cover = all.FirstOrDefault(p => p.Path.StartsWith(f, StringComparison.OrdinalIgnoreCase));
            var box = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), ClipToBounds = true }; box.SetResourceReference(Border.BackgroundProperty, "Card");
            box.Clip = new RectangleGeometry(new Rect(0, 0, 18, 18), 5, 5);
            if (cover != null) { var im = new Image { Stretch = Stretch.UniformToFill }; box.Child = im; Task.Run(() => Thumbs.Decode(cover.Path, 64)).ContinueWith(t => Dispatcher.BeginInvoke(() => im.Source = t.Result)); }
            var r = SideRow("f:" + f, "", System.IO.Path.GetFileName(f.TrimEnd('\\')) is { Length: > 0 } n ? n : f, box, null, f);
            var folder = f;
            r.MouseRightButtonUp += (_, e) => { if (MessageBox.Show(this, "Remove \"" + folder + "\" from Library?", "Gallery", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { S.Folders.Remove(folder); S.Save(); if (view == "f:" + folder) view = "lib"; _ = Rescan(); } };
            st.Children.Add(r);
        }
        foreach (var a in S.Albums.Keys.ToList())
        {
            var r = SideRow("a:" + a, "album", a, null, S.Albums[a].Count);
            var name = a;
            r.MouseRightButtonUp += (_, e) => { if (MessageBox.Show(this, "Delete album \"" + name + "\"? Photos are not deleted.", "Gallery", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { S.Albums.Remove(name); S.Save(); if (view == "a:" + name) view = "lib"; BuildSide(); Rebuild(); } };
            st.Children.Add(r);
        }
        st.Children.Add(SideH("Utilities")); st.Children.Add(SideRow("hidden", "eyeoff", "Hidden"));
    }
    void SetView(string v) { view = v; drillY = drillM = null; sel.Clear(); BuildSide(); Rebuild(); ScrollTop(); }
    void ScrollTop() => inner?.ScrollToVerticalOffset(0);
    void AddFolder_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Add Folder" };
        if (d.ShowDialog(this) == true && !S.Folders.Contains(d.FolderName)) { S.Folders.Add(d.FolderName); S.Save(); _ = Rescan(); }
    }

    // ---------- data ----------
    static bool IsShot(Photo p) => System.Text.RegularExpressions.Regex.IsMatch(p.Name, "screen ?shot|capture", System.Text.RegularExpressions.RegexOptions.IgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(p.Path, @"[\\/]screenshots?[\\/]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static bool IsAnim(Photo p) => p.Name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    string EffMode => view == "recent" ? "all" : mode;
    bool Matches(Photo p, string q)
    {
        if (p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Path.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        var d = p.Date; if (d.Year.ToString() == q || Months[d.Month - 1].StartsWith(q, StringComparison.OrdinalIgnoreCase)) return true;
        if (q.StartsWith("screenshot")) return IsShot(p); if (q is "gif" or "animated") return IsAnim(p);
        if (q is "favorite" or "favourite") return p.IsFav;
        return S.Albums.Any(a => a.Key.Contains(q, StringComparison.OrdinalIgnoreCase) && a.Value.Contains(p.Path));
    }
    List<Photo> Filtered()
    {
        IEnumerable<Photo> l;
        var hid = new HashSet<string>(S.Hidden, StringComparer.OrdinalIgnoreCase);
        if (view == "hidden") l = all.Where(p => hid.Contains(p.Path));
        else
        {
            l = all.Where(p => !hid.Contains(p.Path));
            if (view == "fav") l = l.Where(p => p.IsFav);
            else if (view == "shots") l = l.Where(IsShot);
            else if (view == "anim") l = l.Where(IsAnim);
            else if (view.StartsWith("f:")) { var f = view[2..]; l = l.Where(p => p.Path.StartsWith(f, StringComparison.OrdinalIgnoreCase)); }
            else if (view.StartsWith("a:")) { var set = new HashSet<string>(S.Albums.GetValueOrDefault(view[2..]) ?? new(), StringComparer.OrdinalIgnoreCase); l = l.Where(p => set.Contains(p.Path)); }
        }
        var q = SearchTb.Text.Trim().ToLowerInvariant();
        if (q.Length > 0) l = l.Where(p => Matches(p, q));
        l = l.OrderByDescending(p => p.Mtime);
        if (view == "recent") l = l.Take(100);
        if (drillY != null) l = l.Where(p => p.Date.Year == drillY && (drillM == null || p.Date.Month - 1 == drillM));
        return l.ToList();
    }
    string TitleStr()
    {
        if (drillY != null) return drillM != null ? Months[drillM.Value] + " " + drillY : drillY.ToString()!;
        return view switch { "lib" => "Library", "fav" => "Favorites", "recent" => "Recents", "shots" => "Screenshots", "anim" => "Animated", "hidden" => "Hidden", "deleted" => "Recently Deleted", _ => view.StartsWith("f:") ? System.IO.Path.GetFileName(view[2..].TrimEnd('\\')) : view.StartsWith("a:") ? view[2..] : "Library" };
    }

    // ---------- rows ----------
    void UpdateSeg() => SyncSeg();
    void Mode_Click(object s, RoutedEventArgs e) { SetModeTag(((Button)s).Content.ToString()!); }
    void SetModeTag(string label)
    {
        mode = label switch { "Years" => "years", "Months" => "months", "Days" => "days", _ => "all" };
        drillY = drillM = null; S.Mode = mode; S.Save(); Rebuild(); ScrollTop();
    }
    void SyncSeg()
    {
        foreach (Button b in ModeSeg.Children) { var m = b.Content.ToString() switch { "Years" => "years", "Months" => "months", "Days" => "days", _ => "all" }; b.Tag = m == mode ? "on" : "off"; }
    }
    void Back_Click(object s, RoutedEventArgs e)
    {
        if (drillM != null) { drillM = null; mode = "months"; } else { drillY = null; mode = "years"; }
        Rebuild(); ScrollTop();
    }
    double ViewW => Math.Max(0, (inner?.ViewportWidth > 0 ? inner.ViewportWidth : Rows.ActualWidth - 10));
    void Scroller_SizeChanged(object s, SizeChangedEventArgs e) { if (ready && e.WidthChanged) RowsRelayout(); }
    void RowsRelayout() { rebuildTimer ??= new DispatcherTimerLite(this, 60, () => Rebuild(true)); rebuildTimer.Kick(); }
    void Zoom_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!ready) return; zoom = e.NewValue; S.Zoom = zoom; RowsRelayout(); }
    void Search_Changed(object s, TextChangedEventArgs e) { SearchPh.Visibility = SearchTb.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible; if (ready) { sel.Clear(); Rebuild(); ScrollTop(); } }
    void Search_Focus(object s, RoutedEventArgs e) { SearchBox.BeginAnimation(WidthProperty, new DoubleAnimation(SearchTb.IsFocused ? 230 : 140, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }); }

    void Rebuild(bool keepScroll = false)
    {
        if (!ready) return;
        double off = keepScroll && inner != null ? inner.VerticalOffset : 0;
        list = view == "deleted" ? new List<Photo>() : Filtered();
        string m = EffMode; double w = ViewW; if (w <= 0) return;
        var rows = new List<RowItem>();
        if (m == "all")
        {
            int cols = Math.Max(1, (int)Math.Round(w / zoom)); double size = (w - (cols - 1) * 2) / cols;
            size = Math.Floor(size * 100) / 100;
            for (int i = 0; i < list.Count; i += cols) rows.Add(new TileRow { Items = list.Skip(i).Take(cols).ToArray(), Size = size, Gap = 2 });
        }
        else if (m == "days")
        {
            double ts = Math.Round(zoom * 0.7), avail = w - 32, gap = 4;
            foreach (var g in list.GroupBy(p => p.Date.Date))
            {
                var items = g.ToList();
                rows.Add(new HeaderRow { Text = items[0].Date.ToString("dddd d MMMM yyyy", GB), Sub = items.Count.ToString() });
                int i = 0;
                while (i < items.Count)
                {
                    var cur = new List<Photo>(); double used = 0;
                    while (i < items.Count)
                    {
                        double tw = Math.Min(ts * 3, Math.Round(ts * (items[i].Ar > 0 ? items[i].Ar : 1)));
                        if (cur.Count > 0 && used + gap + tw > avail) break;
                        cur.Add(items[i]); used += (cur.Count > 1 ? gap : 0) + tw; i++;
                    }
                    var ws = cur.Select(p => Math.Min(ts * 3, Math.Round(ts * (p.Ar > 0 ? p.Ar : 1)))).ToArray();
                    bool last = i >= items.Count;
                    if (!last) { double extra = avail - used - 0.5, sumAr = ws.Sum(); if (extra > 0) for (int k = 0; k < ws.Length; k++) ws[k] = Math.Floor(ws[k] + extra * ws[k] / sumAr); }
                    rows.Add(new TileRow { Items = cur.ToArray(), Size = ts, Gap = gap, Left = 16, Widths = ws });
                }
            }
        }
        else if (m == "months")
        {
            int cols = Math.Max(1, (int)Math.Floor((w - 32 + 10) / (420 + 10))); double cw = Math.Floor((w - 32 - (cols - 1) * 10) / cols);
            var cards = list.GroupBy(p => p.Date.Year * 12 + p.Date.Month - 1).Select(g =>
            {
                int y = g.Key / 12, mo = g.Key % 12; var items = g.ToList();
                return new Card { Title = Months[mo] + " " + y, Sub = items.Count + " photos", Photos = items.Take(items.Count < 3 ? 1 : 5).ToArray(), Click = () => { drillY = y; drillM = mo; mode = "days"; Rebuild(); ScrollTop(); } };
            }).ToList();
            for (int i = 0; i < cards.Count; i += cols) rows.Add(new CardRow { Items = cards.Skip(i).Take(cols).ToArray(), W = cw, H = 280 });
        }
        else
        {
            int cols = Math.Max(1, (int)Math.Floor((w - 32 + 10) / (340 + 10))); double cw = Math.Floor((w - 32 - (cols - 1) * 10) / cols);
            var cards = list.GroupBy(p => p.Date.Year).Select(g =>
            {
                int y = g.Key; var items = g.ToList();
                return new Card { Title = y.ToString(), Sub = items.Count + " photos", Photos = new[] { items[0] }, Click = () => { drillY = y; drillM = null; mode = "months"; Rebuild(); ScrollTop(); } };
            }).ToList();
            for (int i = 0; i < cards.Count; i += cols) rows.Add(new CardRow { Items = cards.Skip(i).Take(cols).ToArray(), W = cw, H = Math.Round(cw * 9 / 16), Year = true });
        }
        if (view == "deleted") { rows = TrashRows(); }
        TitleText_Set(); SyncSeg();
        BackBtn.Visibility = drillY != null ? Visibility.Visible : Visibility.Collapsed;
        EmptyBox.Visibility = list.Count == 0 && view != "deleted" ? Visibility.Visible : Visibility.Collapsed;
        Rows.ItemsSource = rows; if (firstRowsMs == 0 && rows.Count > 0) firstRowsMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        if (keepScroll && off > 0) Dispatcher.BeginInvoke(() => inner?.ScrollToVerticalOffset(off), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    void TitleText_Set() { TitleText.Text = sel.Count > 1 ? sel.Count + " Selected" : TitleStr(); }

    // ---------- tiles ----------
    int ThumbPx(double size) { double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; double need = size * dpi; return need <= 256 ? 256 : need <= 384 ? 384 : 512; }
    Image ThumbImage(Photo p, int px, out Action hook)
    {
        var img = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Linear);
        img.SetBinding(Image.SourceProperty, new Binding("Thumb") { Source = p });
        img.Loaded += (_, _) => Thumbs.Request(p, px);
        img.Unloaded += (_, _) => Thumbs.Release(p);
        hook = () => { };
        return img;
    }
    public FrameworkElement MakeTile(Photo p, double size, double? width = null)
    {
        var bd = new Border { Width = width ?? size, Height = size, ClipToBounds = true, Cursor = Cursors.Hand };
        bd.SetResourceReference(Border.BackgroundProperty, "Card");
        var g = new Grid(); bd.Child = g;
        g.Children.Add(ThumbImage(p, ThumbPx(Math.Max(size, width ?? size)), out _));
        // everything except the picture is created lazily (only when selected / favourite / hovered) to keep scrolling fast
        Border? outline = null; Gallery.Icon? badge = null; Button? fav = null, del = null;
        Button Act(string ico, HorizontalAlignment ha, Brush hover, Action act)
        {
            var b = new Button { Width = 26, Height = 26, Visibility = Visibility.Collapsed, HorizontalAlignment = ha, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6), Cursor = Cursors.Hand };
            b.Template = ActTemplate(hover); b.Content = Gallery.Icon.Make(ico, 16, Brushes.White); b.Click += (_, e) => { e.Handled = true; act(); }; b.MouseLeftButtonUp += (_, e) => e.Handled = true; return b;
        }
        void sync()
        {
            if (p.IsSel && outline == null) { outline = new Border { BorderThickness = new Thickness(3), IsHitTestVisible = false }; outline.SetResourceReference(Border.BorderBrushProperty, "Accent"); g.Children.Add(outline); }
            if (outline != null) outline.Visibility = p.IsSel ? Visibility.Visible : Visibility.Collapsed;
            if (p.IsFav && badge == null) { badge = Gallery.Icon.Make("heart", 15, Brushes.White); badge.Fill = Brushes.White; badge.HorizontalAlignment = HorizontalAlignment.Left; badge.VerticalAlignment = VerticalAlignment.Bottom; badge.Margin = new Thickness(6, 0, 0, 5); g.Children.Add(badge); }
            if (badge != null) badge.Visibility = p.IsFav ? Visibility.Visible : Visibility.Collapsed;
            if (fav != null) ((Gallery.Icon)fav.Content).Fill = p.IsFav ? Brushes.White : null!;
        }
        System.ComponentModel.PropertyChangedEventHandler h = (_, e) => { if (e.PropertyName is "IsSel" or "IsFav") sync(); };
        bd.Loaded += (_, _) => { p.PropertyChanged += h; if (p.IsSel || p.IsFav) sync(); };
        bd.Unloaded += (_, _) => p.PropertyChanged -= h;
        bd.MouseEnter += (_, _) =>
        {
            if (fav == null)
            {
                fav = Act("heart", HorizontalAlignment.Left, new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), () => ToggleFav(p));
                del = Act("trash", HorizontalAlignment.Right, new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)), () => DoDelete(new List<Photo> { p }));
                g.Children.Add(fav); g.Children.Add(del); sync();
            }
            fav.Visibility = del!.Visibility = Visibility.Visible;
        };
        bd.MouseLeave += (_, _) => { if (fav != null) fav.Visibility = del!.Visibility = Visibility.Collapsed; };
        bd.MouseLeftButtonUp += (_, e) => TileClick(p, e);
        bd.MouseRightButtonUp += (_, e) => { if (!p.IsSel) { SelectOnly(p); } ShowMenu(p); e.Handled = true; };
        return bd;
    }
    ControlTemplate ActTemplate(Brush hover)
    {
        var t = new ControlTemplate(typeof(Button));
        var b = new FrameworkElementFactory(typeof(Border), "b"); b.SetValue(Border.CornerRadiusProperty, new CornerRadius(13)); b.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x6B, 0, 0, 0))); b.SetValue(Border.PaddingProperty, new Thickness(5));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter)); b.AppendChild(cp); t.VisualTree = b;
        var tr = new Trigger { Property = IsMouseOverProperty, Value = true }; tr.Setters.Add(new Setter(Border.BackgroundProperty, hover, "b")); t.Triggers.Add(tr);
        return t;
    }
    public FrameworkElement MakeCard(Card c, double w, double h, bool year)
    {
        var bd = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, ClipToBounds = true };
        bd.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 12, 12);
        var g = new Grid(); bd.Child = g; bd.SetResourceReference(Border.BackgroundProperty, "Card");
        int px = ThumbPx(year ? w : w / 2);
        if (year || c.Photos.Length < 3) g.Children.Add(ThumbImage(c.Photos[0], ThumbPx(w), out _));
        else
        {
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition()); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            var big = ThumbImage(c.Photos[0], ThumbPx(w / 2), out _); Grid.SetColumnSpan(big, 2); Grid.SetRowSpan(big, 2); big.Margin = new Thickness(0, 0, 1.5, 0); g.Children.Add(big);
            for (int i = 1; i < c.Photos.Length; i++) { var im = ThumbImage(c.Photos[i], 256, out _); Grid.SetColumn(im, 2 + (i - 1) % 2); Grid.SetRow(im, (i - 1) / 2); im.Margin = new Thickness(1.5); g.Children.Add(im); }
        }
        var ov = new Border { VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(18, 40, 18, 14), IsHitTestVisible = false, Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(0x8C, 0, 0, 0), 90) };
        var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = c.Title, Foreground = Brushes.White, FontSize = year ? 32 : 24, FontWeight = FontWeights.Bold }); sp.Children.Add(new TextBlock { Text = c.Sub, Foreground = Brushes.White, Opacity = 0.85, FontSize = 12 });
        ov.Child = sp; if (g.ColumnDefinitions.Count > 0) { Grid.SetColumnSpan(ov, g.ColumnDefinitions.Count); Grid.SetRowSpan(ov, g.RowDefinitions.Count); } g.Children.Add(ov);
        bd.MouseLeftButtonUp += (_, _) => c.Click?.Invoke();
        bd.MouseEnter += (_, _) => bd.Opacity = 0.93; bd.MouseLeave += (_, _) => bd.Opacity = 1;
        return bd;
    }

    // ---------- selection / actions ----------
    void SelectOnly(Photo p) { foreach (var x in sel) x.IsSel = false; sel.Clear(); sel.Add(p); p.IsSel = true; anchor = p; TitleText_Set(); }
    void ClearSel() { foreach (var x in sel) x.IsSel = false; sel.Clear(); TitleText_Set(); }
    void TileClick(Photo p, MouseButtonEventArgs e)
    {
        var mod = Keyboard.Modifiers;
        if ((mod & ModifierKeys.Control) != 0) { if (sel.Remove(p)) p.IsSel = false; else { sel.Add(p); p.IsSel = true; } anchor = p; TitleText_Set(); }
        else if ((mod & ModifierKeys.Shift) != 0 && anchor != null)
        {
            int a = list.IndexOf(anchor), b = list.IndexOf(p); if (a < 0 || b < 0) return;
            foreach (var x in sel) x.IsSel = false; sel.Clear();
            for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) { sel.Add(list[i]); list[i].IsSel = true; }
            TitleText_Set();
        }
        else { ClearSel(); anchor = p; external = false; OpenLb(list.IndexOf(p)); }
    }
    void ToggleFav(Photo p)
    {
        p.IsFav = !p.IsFav; if (p.IsFav) S.Favs.Add(p.Path); else S.Favs.RemoveAll(x => x.Equals(p.Path, StringComparison.OrdinalIgnoreCase)); S.Save();
        if (view == "fav" && Lightbox.Visibility != Visibility.Visible) Rebuild(true);
        LbSyncFav();
    }
    void ShowMenu(Photo p)
    {
        var targets = sel.Count > 0 ? sel.ToList() : new List<Photo> { p };
        var m = new ContextMenu();
        MenuItem Mi(string t, Action a) { var i = new MenuItem { Header = t }; i.Click += (_, _) => a(); return i; }
        m.Items.Add(Mi("Open", () => { external = false; OpenLb(list.IndexOf(p)); }));
        bool allFav = targets.All(x => x.IsFav);
        m.Items.Add(Mi(allFav ? "Remove from Favorites" : "Add to Favorites", () => { foreach (var x in targets) if (x.IsFav == allFav) ToggleFav(x); }));
        bool isHidden = view == "hidden";
        m.Items.Add(Mi(isHidden ? "Unhide" : "Hide", () => HidePhotos(targets, !isHidden)));
        var alb = new MenuItem { Header = "Add to Album" };
        foreach (var a in S.Albums.Keys.ToList()) { var an = a; alb.Items.Add(Mi(a, () => AddToAlbum(an, targets))); }
        alb.Items.Add(new Separator()); alb.Items.Add(Mi("New Album…", () => { var n = Prompt("New Album", "Album name"); if (!string.IsNullOrWhiteSpace(n)) { S.Albums.TryAdd(n.Trim(), new()); AddToAlbum(n.Trim(), targets); } }));
        m.Items.Add(alb);
        m.Items.Add(new Separator());
        m.Items.Add(Mi("Show in Explorer", () => Process.Start("explorer.exe", "/select,\"" + p.Path + "\"")));
        m.Items.Add(Mi("Copy Image", () => { try { var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(p.Path); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); Clipboard.SetImage(b); Toast("Copied"); } catch { Toast("Couldn't copy"); } }));
        m.Items.Add(Mi("Copy Path", () => Clipboard.SetText(p.Path)));
        m.Items.Add(new Separator());
        string nn = targets.Count > 1 ? targets.Count + " Photos" : "Photo"; m.Items.Add(Mi("Move " + nn + " to Recycle Bin", () => _ = DeletePhotos(targets)));
        var del = Mi("Delete " + nn + " Permanently", () => ConfirmPermanent(targets)); del.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)); m.Items.Add(del);
        m.IsOpen = true;
    }
    void AddToAlbum(string name, List<Photo> ps)
    {
        var l = S.Albums.TryGetValue(name, out var x) ? x : S.Albums[name] = new();
        foreach (var p in ps) if (!l.Contains(p.Path)) l.Add(p.Path);
        S.Save(); BuildSide(); Toast("Added to " + name);
    }
    string? Prompt(string title, string label)
    {
        var w = new Window { Title = title, Width = 320, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
        w.SetResourceReference(BackgroundProperty, "Pop");
        var sp = new StackPanel { Margin = new Thickness(16) }; var tb = new TextBox { Padding = new Thickness(6) }; var ok = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(16, 5, 16, 5), HorizontalAlignment = HorizontalAlignment.Right };
        var lbl = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) }; lbl.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        sp.Children.Add(lbl); sp.Children.Add(tb); sp.Children.Add(ok); w.Content = sp; string? res = null; ok.Click += (_, _) => { res = tb.Text; w.Close(); }; w.Loaded += (_, _) => tb.Focus(); w.ShowDialog(); return res;
    }
    void HidePhotos(List<Photo> ps, bool hide)
    {
        foreach (var p in ps) { S.Hidden.RemoveAll(x => x.Equals(p.Path, StringComparison.OrdinalIgnoreCase)); if (hide) S.Hidden.Add(p.Path); }
        S.Save(); sel.Clear(); int wasIdx = idx; bool lb = Lightbox.Visibility == Visibility.Visible; Rebuild(true);
        if (lb) { lbList = list; if (lbList.Count == 0) CloseLb(); else { idx = Math.Min(wasIdx, lbList.Count - 1); ShowLb(); } }
        Toast(hide ? "Hidden" : "Unhidden");
    }
    void ConfirmPermanent(List<Photo> ps) => _ = DeletePhotos(ps, true);
    public static string LastDeleteError = "";
    async Task DeletePhotos(IEnumerable<Photo> ps, bool permanent = false)
    {
        var done = new List<Photo>(); int blocked = 0;
        foreach (var p in ps.ToList())
        {
            try
            {
                await Task.Run(() => { if (permanent) File.Delete(p.Path); else { int rc = ShellOps.Recycle(p.Path); if (rc != 0 || File.Exists(p.Path)) throw new IOException("recycle failed " + rc); } });
                done.Add(p);
            }
            catch (Exception ex) { blocked++; LastDeleteError = ex.GetType().Name + ": " + ex.Message; }
        }
        if (done.Count == 0) { Toast("Couldn’t delete — Windows blocked it. If the photo is in Pictures/OneDrive, Windows Security “Controlled folder access” may need to allow Gallery."); return; }
        foreach (var p in done)
        {
            S.Favs.RemoveAll(x => x.Equals(p.Path, StringComparison.OrdinalIgnoreCase)); S.Hidden.RemoveAll(x => x.Equals(p.Path, StringComparison.OrdinalIgnoreCase));
            if (false) S.Trashed.Insert(0, JsonSerializer.SerializeToElement(new { path = p.Path, name = p.Name, mtime = p.Mtime, when = DateTimeOffset.Now.ToUnixTimeMilliseconds() }));
            all.Remove(p); byPath.Remove(p.Path); sel.Remove(p);
        }
        if (S.Trashed.Count > 200) S.Trashed.RemoveRange(200, S.Trashed.Count - 200);
        S.Save();
        bool lb = Lightbox.Visibility == Visibility.Visible; int wasIdx = idx; bool ext = external;
        if (!ext) { Rebuild(true); }
        if (lb)
        {
            if (ext) { lbList = lbList.Where(x => !done.Contains(x)).ToList(); }
            else lbList = list;
            if (lbList.Count == 0) CloseLb(); else { idx = Math.Min(wasIdx, lbList.Count - 1); ShowLb(); }
        }
        BuildSide();
        Toast(permanent ? (done.Count > 1 ? done.Count + " photos deleted permanently" : "Deleted permanently") : (done.Count > 1 ? done.Count + " photos moved to the Recycle Bin" : "Moved to the Recycle Bin"));
    }

    // ---------- lightbox ----------
    List<Photo> lbList = new(); int idx = -1, lbToken;
    double zs = 1, ztx, zty; bool panning; Point panStart; double panTx, panTy;
    public void OnExternalOpen(string path)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); Topmost = true; Topmost = false; Focus();
        try { SetForegroundWindow(new WindowInteropHelper(this).Handle); } catch { }
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var photos = ScanDir(System.IO.Path.GetDirectoryName(path)!, false).OrderBy(p => p.Name, new NumericNameComparer()).ToList();
        int i = photos.FindIndex(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); if (i < 0) return;
        photos = photos.Select(p => { if (byPath.TryGetValue(p.Path, out var ex)) return ex; p.IsFav = S.Favs.Contains(p.Path, StringComparer.OrdinalIgnoreCase); return p; }).ToList();
        external = true; lbList = photos; OpenLb(i, true); if (int.TryParse(Environment.GetEnvironmentVariable("GALLERY_ROT"), out int tr)) Dispatcher.BeginInvoke(() => Rotate(tr), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    void OpenLb(int i, bool ext = false)
    {
        if (i < 0) return;
        if (!ext) lbList = list;
        idx = i; Lightbox.Visibility = Visibility.Visible; Lightbox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        ShowLb();
    }
    void CloseLb()
    {
        if (Sliding) StopSlide(); lbToken++; preTask = null; prePath = null; stripHi = -1; Lightbox.Visibility = Visibility.Collapsed; LbImg.Source = null; Strip.Children.Clear(); idx = -1; ResetZoom(); InfoPanel.Visibility = Visibility.Collapsed;
        if (external) { external = false; Rebuild(true); }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }
    void LbClose_Click(object s, RoutedEventArgs e) => CloseLb();
    async void ShowLb()
    {
        if (idx < 0 || idx >= lbList.Count) return;
        var p = lbList[idx]; int tok = ++lbToken;
        ResetZoom();
        LbDate.Text = p.Date.ToString("dddd d MMMM yyyy", GB); LbTime.Text = p.Date.ToString("HH:mm", GB);
        LbRot.Angle = S.Rot.GetValueOrDefault(p.Path);
        LbImg.Source = p.Thumb; LbSyncFav();
        FillInfo(p, "…");
        BuildStrip();
        (BitmapSource? img, int w, int h) res;
        if (preTask != null && prePath == p.Path) { var pt = preTask; preTask = null; prePath = null; res = await pt; }
        else { int fit = FitPx(); res = await Task.Run(() => Thumbs.DecodeFull(p.Path, fit)); }
        var (img, w, h) = res; lbHi = false; lbOrigW = w;
        if (tok != lbToken) return;
        LbImg.Source = img; lbDecodedTok = tok; FillInfo(p, img == null ? "—" : w + " × " + h);
        FreeNative(); StartPrefetch();
        if (System.IO.Path.GetExtension(p.Path).ToLowerInvariant() is ".heic" or ".heif" or ".tif" or ".tiff") { quickTrim ??= new DispatcherTimerLite(this, 700, () => Task.Run(() => { try { GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Forced, true, true); } catch { } })); quickTrim.Kick(); }
        if (img == null) Toast("Cannot open this file" + (p.Path.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) || p.Path.EndsWith(".heif", StringComparison.OrdinalIgnoreCase) ? " — install “HEIF Image Extensions” from the Microsoft Store" : ""));
    }
    int FitPx() { double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; return (int)Math.Clamp(Math.Ceiling(Math.Max(LbStageHost.ActualWidth, LbStageHost.ActualHeight) * dpi / 64) * 64, 1280, 2560); }
    DispatcherTimerLite? quickTrim;
    int freeing;
    // decoded bitmaps live in native memory the GC cannot see: collect right after a photo swap so they do not pile up while stepping
    void FreeNative() { if (Interlocked.Exchange(ref freeing, 1) == 1) return; Task.Run(() => { try { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); } catch { } finally { Interlocked.Exchange(ref freeing, 0); } }); }
    Task<(BitmapSource? img, int w, int h)>? preTask; string? prePath; int lastDir = 1;
    void StartPrefetch()
    {
        if (lbList.Count < 2) return; int ni = ((idx + lastDir) % lbList.Count + lbList.Count) % lbList.Count; var np = lbList[ni]; if (prePath == np.Path) return;
        if (System.IO.Path.GetExtension(np.Path).ToLowerInvariant() is ".heic" or ".heif" or ".tif" or ".tiff") return; // Windows codecs for these keep a lot of native memory: decode only on demand
        int fit = FitPx(); prePath = np.Path; preTask = Task.Run(() => Thumbs.DecodeFull(np.Path, fit));
    }
    void FillInfo(Photo p, string dim)
    {
        InfoRows.Children.Clear();
        void KV(string k, string v)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 0) }; var b = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 7, 0, 7), Child = sp }; b.SetResourceReference(Border.BorderBrushProperty, "Sep");
            var a = new TextBlock { Text = k, FontSize = 11 }; a.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var c = new TextBlock { Text = v, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap }; c.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            sp.Children.Add(a); sp.Children.Add(c); InfoRows.Children.Add(b);
        }
        KV("Name", p.Name); KV("Date", p.Date.ToString("dddd d MMMM yyyy", GB) + ", " + p.Date.ToString("HH:mm", GB));
        KV("Size", p.Size > 1048576 ? (p.Size / 1048576.0).ToString("0.0") + " MB" : Math.Round(p.Size / 1024.0) + " KB"); KV("Dimensions", dim);
        KV("Location", System.IO.Path.GetDirectoryName(p.Path) ?? ""); KV("Photo", (idx + 1) + " of " + lbList.Count + (IsShot(p) ? " · Screenshot" : "") + (IsAnim(p) ? " · Animated" : ""));
    }
    int stripLo = 0, stripHi = -1;
    void StyleStrip(Border bd, bool isCur)
    {
        bd.Margin = isCur ? new Thickness(8, 0, 8, 0) : new Thickness(1, 0, 1, 0); bd.Opacity = isCur ? 1 : 0.7;
        bd.RenderTransform = isCur ? new ScaleTransform(1.3, 1.3) : Transform.Identity; bd.BorderThickness = isCur ? new Thickness(2) : new Thickness(0);
        if (isCur) bd.SetResourceReference(Border.BorderBrushProperty, "Accent"); else bd.ClearValue(Border.BorderBrushProperty);
    }
    void BuildStrip()
    {
        // rebuild the filmstrip only when the current photo gets near its edge; otherwise just move the highlight (avoids re-decoding 31 thumbnails per step)
        if (stripHi < 0 || idx < stripLo + 3 && stripLo > 0 || idx > stripHi - 3 && stripHi < lbList.Count - 1 || idx < stripLo || idx > stripHi)
        {
            Strip.Children.Clear();
            stripLo = Math.Max(0, idx - 15); stripHi = Math.Min(lbList.Count - 1, idx + 15);
            for (int k = stripLo; k <= stripHi; k++)
            {
                var p = lbList[k]; int kk = k;
                var im = ThumbImage(p, 256, out _);
                var bd = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(4), ClipToBounds = true, Cursor = Cursors.Hand, Child = im, RenderTransformOrigin = new Point(.5, .5), Tag = k };
                bd.Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 4, 4); bd.SetResourceReference(Border.BackgroundProperty, "Card");
                bd.MouseLeftButtonUp += (_, _) => { idx = kk; ShowLb(); };
                Strip.Children.Add(bd);
            }
        }
        FrameworkElement? cur = null;
        foreach (Border bd in Strip.Children) { bool isCur = (int)bd.Tag == idx; StyleStrip(bd, isCur); if (isCur) cur = bd; }
        Dispatcher.BeginInvoke(() => { if (cur != null) { var pt = cur.TranslatePoint(new Point(0, 0), Strip); StripScroll.ScrollToHorizontalOffset(pt.X - StripScroll.ActualWidth / 2 + 24); } }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    void LbSyncFav() { if (idx >= 0 && idx < lbList.Count) { bool f = lbList[idx].IsFav; LbFavIcon.Fill = f ? new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)) : null!; LbFavIcon.Foreground = f ? new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)) : (Brush)FindResource("Text"); } }
    void Step(int d) { if (lbList.Count == 0) return; lastDir = d < 0 ? -1 : 1; idx = (idx + d + lbList.Count) % lbList.Count; ShowLb(); }
    void LbPrev_Click(object s, RoutedEventArgs e) => Step(-1);
    void LbNext_Click(object s, RoutedEventArgs e) => Step(1);
    void LbFav_Click(object s, RoutedEventArgs e) { if (idx >= 0) ToggleFav(lbList[idx]); }
    void LbInfo_Click(object s, RoutedEventArgs e) { InfoPanel.Visibility = InfoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; LbStageHost.Margin = new Thickness(0, 52, InfoPanel.Visibility == Visibility.Visible ? 280 : 0, 84); }
    void LbDel_Click(object s, RoutedEventArgs e) { if (idx >= 0) DoDelete(new List<Photo> { lbList[idx] }); }
    void LbReveal_Click(object s, RoutedEventArgs e) { if (idx >= 0) Process.Start("explorer.exe", "/select,\"" + lbList[idx].Path + "\""); }
    void LbRotL_Click(object s, RoutedEventArgs e) => Rotate(-90);
    void LbRotR_Click(object s, RoutedEventArgs e) => Rotate(90);
    void Rotate(int d)
    {
        if (idx < 0) return; var p = lbList[idx]; int a = ((S.Rot.GetValueOrDefault(p.Path) + d) % 360 + 360) % 360; if (a == 0) S.Rot.Remove(p.Path); else S.Rot[p.Path] = a; S.Save();
        LbRot.BeginAnimation(RotateTransform.AngleProperty, null); LbRot.Angle = a; ResetZoom();
    }
    void ResetZoom() { zs = 1; ztx = zty = 0; ApplyZoom(); }
    void ApplyZoom() { LbScale.ScaleX = LbScale.ScaleY = zs; LbTrans.X = ztx; LbTrans.Y = zty; }
    bool lbHi; int lbOrigW;
    async void UpgradeHi()
    {
        if (lbHi || idx < 0) return; lbHi = true; int tok = lbToken; var p = lbList[idx];
        var (img, _, _) = await Task.Run(() => Thumbs.DecodeFull(p.Path, 4096));
        if (tok == lbToken && img != null) LbImg.Source = img;
    }
    void ZoomAt(double f, Point host)
    {
        if (f > 1) UpgradeHi();
        double ns = Math.Clamp(zs * f, 1, 16); if (ns == zs) return;
        double cx = host.X - LbStageHost.ActualWidth / 2, cy = host.Y - LbStageHost.ActualHeight / 2;
        ztx = cx - (cx - ztx) * (ns / zs); zty = cy - (cy - zty) * (ns / zs); zs = ns; if (zs <= 1.0001) { zs = 1; ztx = zty = 0; }
        ApplyZoom();
    }
    void Lb_Wheel(object s, MouseWheelEventArgs e) => ZoomAt(e.Delta > 0 ? 1.25 : 0.8, e.GetPosition(LbStageHost));
    void Lb_Down(object s, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { if (zs > 1) ResetZoom(); else ZoomAt(2.5, e.GetPosition(LbStageHost)); return; }
        if (zs > 1) { panning = true; panStart = e.GetPosition(LbStageHost); panTx = ztx; panTy = zty; LbStageHost.CaptureMouse(); }
    }
    void Lb_Move(object s, MouseEventArgs e) { if (!panning) return; var p = e.GetPosition(LbStageHost); ztx = panTx + p.X - panStart.X; zty = panTy + p.Y - panStart.Y; ApplyZoom(); }
    void Lb_Up(object s, MouseButtonEventArgs e) { panning = false; LbStageHost.ReleaseMouseCapture(); }

    void Window_PreviewKeyDown(object s, KeyEventArgs e)
    {
        if (editor != null) return;
        bool lb = Lightbox.Visibility == Visibility.Visible; var k = e.Key; bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0, ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (SearchTb.IsKeyboardFocused) { if (k == Key.Escape) { SearchTb.Text = ""; Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); e.Handled = true; } return; }
        if (lb)
        {
            e.Handled = true;
            switch (k)
            {
                case Key.Escape: if (Sliding) StopSlide(); else if (zs > 1) ResetZoom(); else CloseLb(); break;
                case Key.E: if (idx >= 0) OpenEditor(lbList[idx]); break;
                case Key.Left: Step(-1); break;
                case Key.Right: Step(1); break;
                case Key.F: if (idx >= 0) ToggleFav(lbList[idx]); break;
                case Key.R: Rotate(shift ? -90 : 90); break;
                case Key.I: LbInfo_Click(this, null!); break;
                case Key.Delete: if (idx >= 0) { if (shift) ConfirmPermanent(new List<Photo> { lbList[idx] }); else DoDelete(new List<Photo> { lbList[idx] }); } break;
                case Key.OemPlus: case Key.Add: ZoomAt(1.25, new Point(LbStageHost.ActualWidth / 2, LbStageHost.ActualHeight / 2)); break;
                case Key.OemMinus: case Key.Subtract: ZoomAt(0.8, new Point(LbStageHost.ActualWidth / 2, LbStageHost.ActualHeight / 2)); break;
                case Key.D0: case Key.NumPad0: ResetZoom(); break;
                default: e.Handled = false; break;
            }
            return;
        }
        if (ctrl && k == Key.A) { foreach (var p in list) { sel.Add(p); p.IsSel = true; } TitleText_Set(); e.Handled = true; }
        else if (k == Key.Delete && sel.Count > 0) { if (shift) ConfirmPermanent(sel.ToList()); else DoDelete(sel.ToList()); e.Handled = true; }
        else if (k == Key.Escape && sel.Count > 0) { ClearSel(); e.Handled = true; }
        else if (ctrl && k == Key.F) { SearchTb.Focus(); e.Handled = true; }
    }
}

class NumericNameComparer : IComparer<string>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);
    public int Compare(string? a, string? b) => StrCmpLogicalW(a ?? "", b ?? "");
}

class DispatcherTimerLite
{
    readonly System.Windows.Threading.DispatcherTimer t;
    public DispatcherTimerLite(DependencyObject o, int ms, Action a) { t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; t.Tick += (_, _) => { t.Stop(); a(); }; }
    public void Kick() { t.Stop(); t.Start(); }
}

class LiveLog { readonly string f; public LiveLog(string f) { this.f = f; File.WriteAllText(f, ""); } public void Add(string s) => File.AppendAllText(f, s + Environment.NewLine); }

static class ShellOps
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct OP { public IntPtr h; public uint f; public string from; public string? to; public ushort flags; public int abort; public IntPtr map; public string? title; }
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int SHFileOperationW(ref OP o);
    // Recycle Bin move with every dialog suppressed (no "administrator permission" prompts): returns 0 on success
    public static int Recycle(string path) { var o = new OP { f = 3, from = path + " ", flags = 0x4 | 0x10 | 0x40 | 0x400 }; return SHFileOperationW(ref o); }
}

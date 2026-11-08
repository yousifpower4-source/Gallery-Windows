using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Gallery;

public partial class MainWindow
{
    // ---------- slideshow ----------
    DispatcherTimer? slideT; bool slideBusy;
    bool Sliding => slideT != null;
    void LbPlay_Click(object s, RoutedEventArgs e) { if (Sliding) StopSlide(); else StartSlide(); }
    void StartSlide()
    {
        if (idx < 0 || lbList.Count < 2) return;
        Toast("Slideshow — Esc to stop"); LbBar.Opacity = 0; LbBar.IsHitTestVisible = false; StripScroll.Opacity = 0; StripScroll.IsHitTestVisible = false;
        slideT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        slideT.Tick += (_, _) =>
        {
            if (slideBusy) return; slideBusy = true;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)); fade.Completed += (_, _) => { if (!Sliding) { slideBusy = false; return; } Step(1); LbImg.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))); slideBusy = false; };
            LbImg.BeginAnimation(OpacityProperty, fade);
        };
        slideT.Start();
    }
    void StopSlide()
    {
        slideT?.Stop(); slideT = null; slideBusy = false; LbImg.BeginAnimation(OpacityProperty, null); LbImg.Opacity = 1;
        LbBar.Opacity = 1; LbBar.IsHitTestVisible = true; StripScroll.Opacity = 1; StripScroll.IsHitTestVisible = true;
    }

    // ---------- editor ----------
    Editor? editor;
    void LbEdit_Click(object s, RoutedEventArgs e) { if (idx >= 0) OpenEditor(lbList[idx]); }
    void OpenEditor(Photo p)
    {
        if (editor != null) return; if (Sliding) StopSlide();
        editor = new Editor(this, p, outPath => CloseEditor(outPath));
        Root.Children.Add(editor); editor.Focus();
    }
    void CloseEditor(string? outPath)
    {
        if (editor == null) return; Root.Children.Remove(editor); editor = null; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        if (outPath == null) { Focus(); return; }
        try
        {
            var fi = new FileInfo(outPath);
            var np = new Photo { Path = fi.FullName, Name = fi.Name, Mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(), Size = fi.Length };
            all.Add(np); byPath[np.Path] = np;
            if (external) { lbList.Add(np); lbList = lbList.OrderBy(x => x.Name, new NumericNameComparer()).ToList(); idx = lbList.IndexOf(np); }
            else { Rebuild(true); lbList = list; idx = Math.Max(0, list.IndexOf(np)); }
            BuildSide(); ShowLb(); Toast("Saved as " + np.Name);
        }
        catch { Toast("Saved"); }
        Focus();
    }

    // ---------- recently deleted ----------
    List<RowItem> TrashRows()
    {
        var rows = new List<RowItem>();
        rows.Add(new HeaderRow { Text = "Recently Deleted", Sub = S.Trashed.Count + "" });
        foreach (var t in S.Trashed)
        {
            string path = t.TryGetProperty("path", out var pp) ? pp.GetString() ?? "" : "", name = t.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : "";
            long when = t.TryGetProperty("when", out var ww) && ww.TryGetInt64(out var w2) ? w2 : 0;
            rows.Add(new TrashRow { Path = path, Name = name, When = when > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(when).LocalDateTime : DateTime.Now });
        }
        return rows;
    }
    public void PurgeTrashed(string path)
    {
        if (MessageBox.Show(this, "Permanently delete “" + System.IO.Path.GetFileName(path) + "” from the Recycle Bin? This can’t be undone.", "Delete Permanently", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        RecycleVerb(path, "delete", "Deleted permanently");
    }
    public void Restore(string path) => RecycleVerb(path, "undelete", "Restored");
    async void RecycleVerb(string path, string verb, string okMsg)
    {
        const string ps = "$o=$env:GAL_PATH; $d=Split-Path $o -Parent; $n=Split-Path $o -Leaf; $b=[IO.Path]::GetFileNameWithoutExtension($n); $sh=New-Object -ComObject Shell.Application; $rb=$sh.NameSpace(10); $ok=$false; foreach($i in $rb.Items()){ $od=$rb.GetDetailsOf($i,1); if($od.TrimEnd('\\') -ieq $d.TrimEnd('\\') -and ($i.Name -ieq $n -or $i.Name -ieq $b)){ $i.InvokeVerb($env:GAL_VERB); $ok=$true; break } }; if($ok){'OK'}";
        bool ok = await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", ps }) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                psi.Environment["GAL_PATH"] = path; psi.Environment["GAL_VERB"] = verb;
                using var p = Process.Start(psi)!; var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(20000); Thread.Sleep(600);
                Thread.Sleep(verb == "delete" ? 600 : 0); return o.Contains("OK") && (verb == "delete" ? !File.Exists(path) : File.Exists(path));
            }
            catch { return false; }
        });
        if (!ok) { Toast(verb == "delete" ? "Couldn't delete" : "Couldn't restore"); return; }
        S.Trashed.RemoveAll(t => t.TryGetProperty("path", out var pp) && string.Equals(pp.GetString(), path, StringComparison.OrdinalIgnoreCase)); S.Save();
        if (verb == "undelete") await Rescan(); else Rebuild(true); Toast(okMsg);
    }
}

public class TrashRow : RowItem
{
    public string Path = "", Name = ""; public DateTime When;
    public override FrameworkElement Build(MainWindow w)
    {
        var g = new Grid { Margin = new Thickness(16, 2, 16, 2), Height = 34 }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var n = new TextBlock { Text = Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; n.SetResourceReference(TextBlock.ForegroundProperty, "Text"); g.Children.Add(n);
        var d = new TextBlock { Text = When.ToString("d MMM yyyy HH:mm", new System.Globalization.CultureInfo("en-GB")), FontSize = 11, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; d.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Grid.SetColumn(d, 1); g.Children.Add(d);
        var b = new Button { Content = "Restore", Padding = new Thickness(12, 4, 12, 4), BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand }; b.SetResourceReference(Control.BackgroundProperty, "Seg"); b.SetResourceReference(Control.ForegroundProperty, "Text"); Grid.SetColumn(b, 2);
        var path = Path; b.Click += (_, _) => w.Restore(path); g.Children.Add(b);
        var pd = new Button { Content = "Delete Permanently", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4), BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)) };
        Grid.SetColumn(pd, 3); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); pd.Click += (_, _) => w.PurgeTrashed(path); g.Children.Add(pd); return g;
    }
}

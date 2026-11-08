using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace Gallery;

public partial class App : Application
{
    Mutex? mutex;
    const string PipeName = "GalleryWpf.Open";
    public static string? StartFile; public static bool EditFlag;

    protected override void OnStartup(StartupEventArgs e)
    {
        string? file = e.Args.FirstOrDefault(a => !a.StartsWith("-") && File.Exists(a));
        mutex = new Mutex(true, "GalleryWpf.Single", out bool first);
        if (!first)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                c.Connect(2000);
                var b = Encoding.UTF8.GetBytes(file != null ? Path.GetFullPath(file) : "");
                c.Write(b, 0, b.Length);
            }
            catch { }
            Shutdown(); return;
        }
        EditFlag = e.Args.Contains("--edit"); StartFile = file != null ? Path.GetFullPath(file) : null;
        if (Environment.GetEnvironmentVariable("GALLERY_HW") != "1") System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        base.OnStartup(e);
        new MainWindow().Show();
        Task.Run(Listen);
    }

    void Listen()
    {
        while (true)
        {
            try
            {
                using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                s.WaitForConnection();
                using var r = new StreamReader(s, Encoding.UTF8);
                var path = r.ReadToEnd().Trim();
                Dispatcher.BeginInvoke(() => Gallery.MainWindow.Instance?.OnExternalOpen(path));
            }
            catch { Thread.Sleep(200); }
        }
    }
}

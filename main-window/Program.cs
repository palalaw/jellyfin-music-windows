using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JellyfinMusic;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Contains("--shutdown")) {
            AppInstance.SendAsync(true).GetAwaiter().GetResult();
            var until=DateTime.UtcNow.AddSeconds(15);
            while(DateTime.UtcNow<until) {
                bool running=false;
                foreach(var process in System.Diagnostics.Process.GetProcessesByName("JellyfinMusic")) {
                    using(process) if(process.Id!=Environment.ProcessId&&process.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId) running=true;
                }
                if(!running) return Task.Run(()=>new TaskbarRuntime().StopAsync()).GetAwaiter().GetResult()?0:3;
                Thread.Sleep(200);
            }
            return 2;
        }
        if(args.Contains("--self-test")) return SelfTest.Run();
        if(args.Contains("--taskbar-test")) return TaskbarBridgeTest.RunAsync().GetAwaiter().GetResult();
        using var instanceMutex=args.Length==0?new Mutex(false,"Local\\JellyfinMusic.Main.v1"):null;
        bool ownsInstance=false;
        if(instanceMutex is not null) {try {ownsInstance=instanceMutex.WaitOne(0);} catch(AbandonedMutexException) {ownsInstance=true;}}
        if(args.Length==0 && !ownsInstance) {
            AppInstance.SendAsync(false).GetAwaiter().GetResult();return 0;
        }
        var app=new Application();
        if(args.Length>=2 && args[0]=="--switch-test") {
            app.Dispatcher.InvokeAsync(async ()=> { int code=await SwitchTest.RunAsync(args[1]); app.Shutdown(code); });
            return app.Run();
        }
        if(args.Length>=2 && args[0]=="--library-test") {
            app.Dispatcher.InvokeAsync(async ()=> { int code=await IntegrationTest.VerifyLibrariesAsync(args[1]); app.Shutdown(code); });
            return app.Run();
        }
        if(args.Length>=2 && args[0]=="--integration-test") {
            app.Dispatcher.InvokeAsync(async ()=> { int code=await IntegrationTest.RunAsync(args[1]); app.Shutdown(code); });
            return app.Run();
        }
        var window=new MainWindow(args.Length==0);
        if(args.Length>=2 && args[0]=="--ui-test") {
            app.Dispatcher.InvokeAsync(async ()=> {
                int code=1;
                try {
                    var report=await window.VerifyNavigationAsync();
                    await File.WriteAllTextAsync(args[1],System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
                    code=report["passed"] is true?0:1;
                } catch(Exception e) { await File.WriteAllTextAsync(args[1],e.GetType().Name+": "+e.Message); }
                app.Shutdown(code);
            });
            return app.Run();
        }
        if(args.Length>=2 && args[0]=="--render-live") {
            app.Dispatcher.InvokeAsync(async ()=> {
                int code=0;
                try {
                    await window.ConnectAsync(Environment.GetEnvironmentVariable("JFM_TEST_SERVER")??"",Environment.GetEnvironmentVariable("JFM_TEST_USER")??"",Environment.GetEnvironmentVariable("JFM_TEST_PASSWORD")??"");
                    await window.PrepareLivePreviewAsync(args.Length>2?args[2]:"albums");
                    var root=(FrameworkElement)window.Content; root.Measure(new Size(1280,820)); root.Arrange(new Rect(0,0,1280,820)); root.UpdateLayout();
                    await Task.Delay(4000);
                    Render(window,args[1]);
                } catch { code=1; }
                finally { await window.CloseConnectionAsync(); app.Shutdown(code); }
            });
            return app.Run();
        }
        if(args.Length>=2 && args[0]=="--render") {
            // Render our own WPF visual tree to an artifact without showing a window
            // or interacting with the user's desktop.
            window.PreparePreview(args.Length>2 ? args[2] : "albums");
            Render(window,args[1]);
            app.Shutdown(); return 0;
        }
        using var instance=args.Length==0?new AppInstance(exit=>window.Dispatcher.InvokeAsync(()=> {
            if(exit) window.ExitApplication(); else {window.Show();window.WindowState=WindowState.Normal;window.Activate();}
        }).Task):null;
        app.Run(window);
        if(args.Length==0) Task.Run(()=>new TaskbarRuntime().StopAsync()).GetAwaiter().GetResult();
        if(ownsInstance) instanceMutex!.ReleaseMutex();
        return 0;
    }
    private static void Render(MainWindow window,string path)
    {
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1280,820)); root.Arrange(new Rect(0,0,1280,820)); root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1280,820,96,96,PixelFormats.Pbgra32); bitmap.Render(root);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path); png.Save(file);
    }
}

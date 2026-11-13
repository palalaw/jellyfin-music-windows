using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;

namespace JellyfinMusic;

// Only manages the copy installed beside this app. Never stops another Windhawk.
public sealed class TaskbarRuntime
{
    public static string Executable => Path.Combine(AppContext.BaseDirectory,"TaskbarRuntime","windhawk.exe");
    public bool Available => File.Exists(Executable);
    private readonly SemaphoreSlim gate=new(1);
    public string? Failure { get; private set; }

    private static List<Process> OwnedProcesses()
    {
        var result=new List<Process>();
        foreach(var process in Process.GetProcessesByName("windhawk")) {
            try {
                if(process.SessionId!=Process.GetCurrentProcess().SessionId) { process.Dispose(); continue; }
                if(!string.Equals(process.MainModule?.FileName,Executable,StringComparison.OrdinalIgnoreCase)) {
                    process.Dispose();
                    foreach(var owned in result) owned.Dispose();
                    throw new InvalidOperationException("別のWindhawkが起動しています。そちらを終了してからタスクバー表示をオンにしてください。");
                }
                result.Add(process);
            } catch(InvalidOperationException) { throw; }
            catch { process.Dispose(); foreach(var owned in result) owned.Dispose(); throw new InvalidOperationException("Windhawkの起動元を確認できないため、連携を開始できません。"); }
        }
        return result;
    }

    public async Task StartAsync()
    {
        if(!Available) return; // Source/ZIP builds can use a manually prepared host.
        await gate.WaitAsync();
        try {
            Failure=null;
            // First installer release is bounded to the taskbar binary verified
            // on the test PC. No proprietary symbol-download tools are bundled.
            using(var stream=File.OpenRead(Path.Combine(Environment.SystemDirectory,"taskbar.dll"))) {
                using var pe=new PEReader(stream);
                bool supported=false;
                foreach(var entry in pe.ReadDebugDirectory()) if(entry.Type==DebugDirectoryEntryType.CodeView) {
                    var cv=pe.ReadCodeViewDebugDirectoryData(entry);
                    supported=cv.Guid==new Guid("23d06b07-e6aa-07e4-e886-6171fca13f62")&&cv.Age==1;
                    if(supported) break;
                }
                if(!supported) throw new InvalidOperationException("このWindowsのタスクバーは未検証です。メイン画面は使用できますが、タスクバー連携は更新版が必要です。");
            }
            var owned=OwnedProcesses();
            bool running=owned.Count>0;
            foreach(var process in owned) process.Dispose();
            if(!running) {
                using var process=Process.Start(new ProcessStartInfo(Executable,"-tray-only") {
                    UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(Executable)!
                }) ?? throw new IOException("連携エンジンを起動できません。");
                await Task.Delay(500);
                if(process.HasExited && process.ExitCode!=0) throw new IOException("連携エンジンの起動に失敗しました。");
            }
        } catch(Exception e) { Failure=e.Message; }
        finally {gate.Release();}
    }

    public async Task<bool> StopAsync()
    {
        if(!Available) return true;
        await gate.WaitAsync();
        try {
            // If another Windhawk is running, do not send its global exit command.
            List<Process> owned;
            try {owned=OwnedProcesses();} catch {return true;}
            try {
                if(owned.Count==0) return true;
                using var stop=Process.Start(new ProcessStartInfo(Executable,"-exit -wait -timeout 10000") {
                    UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(Executable)!
                }) ?? throw new IOException("連携エンジンの終了要求に失敗しました。");
                await stop.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
                foreach(var process in owned) {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                }
                return true;
            } finally {foreach(var process in owned) process.Dispose();}
        } catch(Exception e) {Failure=e.Message;return false;}
        finally {gate.Release();}
    }
}

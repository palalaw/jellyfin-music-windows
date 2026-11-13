using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace JellyfinMusic;
public sealed class AppInstance : IDisposable
{
    private static string Name=>$"JellyfinMusic.Control.v1.{Process.GetCurrentProcess().SessionId}";
    private readonly CancellationTokenSource stop=new();
    public Task Completion { get; }
    public AppInstance(Func<bool,Task> action)=>Completion=ServeAsync(action);
    private async Task ServeAsync(Func<bool,Task> action)
    {
        try {
            while(!stop.IsCancellationRequested) {
                using var pipe=new NamedPipeServerStream(Name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var limit=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);limit.CancelAfter(2000);
                try {
                    var command=new byte[1];await pipe.ReadExactlyAsync(command,limit.Token);
                    if(command[0] is not (1 or 2)) continue;
                    // Reply first: shutdown may dispose the server and dispatcher.
                    await pipe.WriteAsync(new byte[]{1},limit.Token);
                    await action(command[0]==2);
                } catch(IOException) {} catch(OperationCanceledException) when(!stop.IsCancellationRequested) {}
            }
        } catch(OperationCanceledException) {} catch(IOException) {}
    }
    public static async Task<bool> SendAsync(bool exit,int timeout=1500)
    {
        try {
            using var pipe=new NamedPipeClientStream(".",Name,PipeDirection.InOut,PipeOptions.Asynchronous);
            using var limit=new CancellationTokenSource(timeout);
            await pipe.ConnectAsync(limit.Token);await pipe.WriteAsync(new byte[]{exit?(byte)2:(byte)1},limit.Token);
            var response=new byte[1];await pipe.ReadExactlyAsync(response,limit.Token);return response[0]==1;
        } catch(IOException) {return false;} catch(OperationCanceledException) {return false;}
    }
    public void Dispose()=>stop.Cancel();
}

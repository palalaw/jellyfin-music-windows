using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace JellyfinMusic;

public sealed record TaskbarState(bool Visible,bool Playing,bool CanControl,string Title,string Artist);
// Local, current-user-only IPC. No server address, credentials, audio or file paths.
public sealed class TaskbarBridge : IDisposable
{
    public const uint Magic=0x314D464A;
    public const int PacketSize=1048;
    public static string DefaultPipeName=>$"JellyfinMusic.Taskbar.v1.{Process.GetCurrentProcess().SessionId}";
    private readonly CancellationTokenSource stop=new();
    private readonly Func<int,CancellationToken,Task> command;
    private readonly string pipeName;
    private TaskbarState state=new(false,false,false,"","");
    private long lastContact;
    private int displayReady;
    public bool Connected=>Environment.TickCount64-Interlocked.Read(ref lastContact)<2500 && Interlocked.Read(ref lastContact)>0;
    public bool DisplayReady=>Connected && Volatile.Read(ref displayReady)!=0 && Volatile.Read(ref state).Visible;
    public string? Failure { get; private set; }
    public Task Completion { get; }
    public TaskbarBridge(Func<int,CancellationToken,Task> command,string? name=null)
    {
        this.command=command; pipeName=name??DefaultPipeName; Completion=ServeAsync();
    }
    public void Publish(TaskbarState value)=>Volatile.Write(ref state,value);
    public static byte[] Encode(TaskbarState value)
    {
        var bytes=new byte[PacketSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes,Magic);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),value.Visible?1:0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12),value.Playing?1:0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16),value.CanControl?1:0);
        static void Put(string value,Span<byte> target) {
            int length=Math.Min(value.Length,255);
            if(length>0&&char.IsHighSurrogate(value[length-1])) length--;
            Encoding.Unicode.GetBytes(value.AsSpan(0,length),target);
        }
        Put(value.Title,bytes.AsSpan(24,512)); Put(value.Artist,bytes.AsSpan(536,512)); return bytes;
    }
    private async Task ServeAsync()
    {
        try {
            while(!stop.IsCancellationRequested) {
                using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,256,PacketSize);
                await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(1000);
                try {
                    var request=new byte[8]; await pipe.ReadExactlyAsync(request,timeout.Token).ConfigureAwait(false);
                    int flags=BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(4));
                    int action=flags&255;
                    if(BinaryPrimitives.ReadUInt32LittleEndian(request)!=Magic || (flags&~0x1ff)!=0 || action>4) continue;
                    Volatile.Write(ref displayReady,(flags&0x100)!=0?1:0);
                    Interlocked.Exchange(ref lastContact,Environment.TickCount64);
                    var snapshot=Volatile.Read(ref state);
                    if(action!=0&&snapshot.Visible&&(action==4||snapshot.CanControl)) await command(action,timeout.Token).ConfigureAwait(false);
                    await pipe.WriteAsync(Encode(Volatile.Read(ref state)),timeout.Token).ConfigureAwait(false);
                } catch(IOException) { } catch(OperationCanceledException) when(!stop.IsCancellationRequested) { }
            }
        } catch(OperationCanceledException) { }
        catch(Exception e) { Failure=e.GetType().Name; }
    }
    public void Dispose()=>stop.Cancel();
}

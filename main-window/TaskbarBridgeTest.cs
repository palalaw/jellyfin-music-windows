using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace JellyfinMusic;
public static class TaskbarBridgeTest
{
    public static async Task<int> RunAsync()
    {
        string name="JellyfinMusic.Test."+Guid.NewGuid().ToString("N");
        var actions=new List<int>();
        using var bridge=new TaskbarBridge((action,ct)=> { lock(actions) actions.Add(action); return Task.CompletedTask; },name);
        async Task<byte[]> Request(int action) {
            using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
            using var timeout=new CancellationTokenSource(3000);
            await pipe.ConnectAsync(timeout.Token);
            byte[] request=new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(request,TaskbarBridge.Magic); BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4),action);
            await pipe.WriteAsync(request,timeout.Token);
            var response=new byte[TaskbarBridge.PacketSize]; await pipe.ReadExactlyAsync(response,timeout.Token); return response;
        }
        bridge.Publish(new(true,true,true,"テスト曲 🎵","アーティスト"));
        var state=await Request(0);
        if(Encoding.Unicode.GetString(state,24,512).TrimEnd('\0')!="テスト曲 🎵") throw new Exception("Unicode state");
        if(bridge.DisplayReady) throw new Exception("Connected must not imply displayed");
        await Request(0x100);
        if(!bridge.DisplayReady) throw new Exception("Display acknowledgement");
        await Request(0);
        if(bridge.DisplayReady) throw new Exception("Display acknowledgement cleared");
        for(int i=1;i<=4;i++) await Request(i);
        bridge.Publish(new(false,false,true,"","")); await Request(2);
        if(actions.Count!=4||!bridge.Connected) throw new Exception("Visibility command gate");
        bridge.Publish(new(true,false,false,"","")); await Request(1); await Request(4);
        if(actions.Count!=5||actions[^1]!=4) throw new Exception("No-track command gate");
        // A silent client must not permanently occupy the server.
        using(var stalled=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)) {
            await stalled.ConnectAsync(3000); await Task.Delay(1200); await Request(0);
        }
        bridge.Dispose(); await bridge.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        return 0;
    }
}

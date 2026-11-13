using System.Windows.Threading;
namespace JellyfinMusic;
// App policy only. libmpv owns network transport, buffering, codecs and seeking.
public sealed class AudioPlayback : IDisposable
{
    private readonly JellyfinClient client;
    private readonly PlaybackSession session;
    private readonly JellyfinPlaybackService service;
    private IAudioEngine? engine;
    private readonly Func<IAudioEngine> createEngine;
    private CancellationTokenSource? opening;
    private PlaybackSource? source;
    private long revision=-1,generation;
    private bool lastPlaying,started,disposed,canSeek,updating;
    private double volume=.7,position;
    private DateTime lastReport,loadDeadline;
    private readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(100)};
    public event Action<string>? Status;
    public bool HasOpened {get;private set;}
    public bool IsTranscoding=>false;
    public string? LastFailureCode {get;private set;}
    public string? LastFailureDetail {get;private set;}
    public int ReportFailures=>service.ReportFailures;
    public AudioPlayback(JellyfinClient client,PlaybackSession session,Func<IAudioEngine>? factory=null)
    {
        this.client=client;this.session=session;createEngine=factory??(()=>new MpvAudioEngine());
        service=new(client.Export(""));session.Changed+=Sync;session.SeekRequested+=Seek;
        timer.Tick+=(_,_)=> {
            try {engine?.Poll();if(opening is not null&&!HasOpened&&DateTime.UtcNow>loadDeadline)Fail("timeout","再生の読み込みがタイムアウトしました。");}
            catch(Exception e){Fail("engine",e.Message);}
        };timer.Start();
    }
    private void Command(Action action) {try {action();}catch(Exception e){Fail("engine",e.Message);}}
    public void SetVolume(double value) {volume=Math.Clamp(value,0,1);Command(()=>engine?.SetVolume(volume));ReportProgress();}
    public void Seek(double seconds) {position=seconds;if(HasOpened&&canSeek){Command(()=>engine?.Seek(seconds));ReportProgress();}}
    private void Sync()
    {
        if(disposed||updating)return;
        if(session.Current is null){CancelCurrent();revision=session.Revision;return;}
        if(revision!=session.Revision||(!HasOpened&&opening is null&&session.IsPlaying)){revision=session.Revision;_=OpenAsync();return;}
        if(session.IsPlaying!=lastPlaying){lastPlaying=session.IsPlaying;Command(()=>engine?.SetPaused(!lastPlaying));ReportProgress();}
    }
    private void EnsureEngine()
    {
        if(engine is not null)return;
        engine=createEngine();engine.SetVolume(volume);engine.Updated+=Observe;
        engine.Ended+=g=>{if(disposed||g!=generation)return;StopReport(false);HasOpened=false;opening?.Dispose();opening=null;session.Ended();};
        engine.Failed+=(g,error)=>{if(!disposed&&g==generation)Fail(error,"原音を再生できません。変換は行いません。");};
    }
    private async Task OpenAsync()
    {
        CancelCurrent();var g=generation;var track=session.Current;if(track is null)return;
        position=session.Position;lastPlaying=session.IsPlaying;
        var cts=new CancellationTokenSource();opening=cts;loadDeadline=DateTime.UtcNow.AddSeconds(30);
        LastFailureCode=null;LastFailureDetail=null;Status?.Invoke("読み込み中…");
        try {
            EnsureEngine();var resolved=await service.ResolveAsync(track,cts.Token);
            if(disposed||g!=generation||cts.IsCancellationRequested)return;
            source=resolved;engine!.Load(resolved.Uri,service.Authorization,g,session.Position,!session.IsPlaying);
        }catch(OperationCanceledException)when(cts.IsCancellationRequested){}
        catch(Exception e){if(!disposed&&g==generation)Fail(e.GetType().Name,e.Message);}
    }
    private void Observe(AudioObservation state)
    {
        if(disposed||state.Generation!=generation||source is null)return;
        canSeek=state.CanSeek;
        if(!HasOpened){
            HasOpened=true;opening?.Dispose();opening=null;
            if(Math.Abs(session.Position-state.Position)>1)engine!.Seek(session.Position);
            engine!.SetPaused(!session.IsPlaying);
            started=true;service.Start(source,session.Position,!session.IsPlaying,volume,canSeek);lastReport=DateTime.UtcNow;
            Status?.Invoke("再生中 · 原音ストリーム（mpv）");return;
        }
        if(!state.Seeking){position=state.Position;updating=true;try{session.UpdatePosition(position);}finally{updating=false;}}
        if(DateTime.UtcNow-lastReport>TimeSpan.FromSeconds(10))ReportProgress();
    }
    private void ReportProgress(){if(!started||source is null)return;lastReport=DateTime.UtcNow;service.Progress(source,position,!session.IsPlaying,volume,canSeek);}
    private void StopReport(bool failed){if(started&&source is not null)service.Stop(source,position,failed);started=false;source=null;}
    private void CancelCurrent(){generation++;opening?.Cancel();opening?.Dispose();opening=null;StopReport(false);HasOpened=false;
        try{engine?.Stop();}catch{engine?.Dispose();engine=null;}}
    private void Fail(string code,string detail){LastFailureCode=code;LastFailureDetail=client.Redact(detail);StopReport(true);CancelCurrent();session.Pause();Status?.Invoke("原音を再生できません。再生ボタンで再試行できます。変換は行いません。");}
    public Task FlushReportsAsync()=>service.FlushAsync();
    public Task<bool> VerifyServerStateAsync(string? itemId,bool paused,double seconds)=>service.VerifyServerStateAsync(itemId,paused,seconds);
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();session.Changed-=Sync;session.SeekRequested-=Seek;CancelCurrent();engine?.Dispose();service.Dispose();}
}

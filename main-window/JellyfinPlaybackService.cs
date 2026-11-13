using System.Net.Http;
using System.Threading.Channels;
using Jellyfin.Sdk;
using Jellyfin.Sdk.Generated.Models;
namespace JellyfinMusic;
public sealed record PlaybackSource(Guid ItemId,string MediaSourceId,string PlaySessionId,Uri Uri);
public sealed class JellyfinPlaybackService : IDisposable
{
    private readonly JellyfinApiClient api;
    private readonly JellyfinSdkSettings settings=new();
    private readonly SavedConnection connection;
    private readonly Channel<Func<CancellationToken,Task>> reports=Channel.CreateUnbounded<Func<CancellationToken,Task>>(new(){SingleReader=true,SingleWriter=true});
    private readonly CancellationTokenSource stopping=new();
    private readonly Task reporter;
    private readonly object progressLock=new();
    private sealed class ProgressSlot {public Func<CancellationToken,Task>? Send;}
    private ProgressSlot? pendingProgress;
    public int ReportFailures {get;private set;}
    public string Authorization=>settings.GetAuthorizationHeader();
    public JellyfinPlaybackService(SavedConnection connection)
    {
        this.connection=connection;
        settings.Initialize("JellyfinMusic","0.6.0-preview.1","Windows",connection.DeviceId);
        settings.SetServerUrl(connection.Address.TrimEnd('/'));settings.SetAccessToken(connection.Token);
        var http=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseDefaultCredentials=false}) {Timeout=TimeSpan.FromSeconds(15)};
        api=new(new JellyfinRequestAdapter(new JellyfinAuthenticationProvider(settings),settings,http));
        reporter=Task.Run(ReportLoop);
    }
    public async Task<PlaybackSource> ResolveAsync(Track track,CancellationToken ct)
    {
        var info=await api.Items[Guid.Parse(track.Id)].PlaybackInfo.PostAsync(new PlaybackInfoDto {
            UserId=Guid.Parse(connection.UserId),EnableDirectPlay=true,EnableDirectStream=false,EnableTranscoding=false,
            AutoOpenLiveStream=false,DeviceProfile=new DeviceProfile {
                Name="JellyfinMusic libmpv original audio",MaxStreamingBitrate=int.MaxValue,MaxStaticBitrate=int.MaxValue,MaxStaticMusicBitrate=int.MaxValue,
                DirectPlayProfiles=[new DirectPlayProfile {Type=DirectPlayProfile_Type.Audio}],TranscodingProfiles=[]
            }
        },cancellationToken:ct).ConfigureAwait(false);
        var source=info?.MediaSources?.FirstOrDefault(s=>s.SupportsDirectPlay==true&&s.RequiresOpening!=true&&!string.IsNullOrEmpty(s.Id));
        if(source is null)throw new InvalidOperationException("原音のDirect Playを利用できません。変換は行いません。");
        var request=api.Audio[Guid.Parse(track.Id)].Stream.ToGetRequestInformation(c=> {
            c.QueryParameters.Static=true;c.QueryParameters.MediaSourceId=source.Id;c.QueryParameters.DeviceId=connection.DeviceId;
        });
        return new(Guid.Parse(track.Id),source.Id!,info!.PlaySessionId??"",api.BuildUri(request));
    }
    private static long Ticks(double position)=>(long)(Math.Max(0,position)*TimeSpan.TicksPerSecond);
    public void Start(PlaybackSource s,double position,bool paused,double volume,bool canSeek)=>reports.Writer.TryWrite(ct=>api.Sessions.Playing.PostAsync(new PlaybackStartInfo {
        ItemId=s.ItemId,MediaSourceId=s.MediaSourceId,PlaySessionId=s.PlaySessionId,PositionTicks=Ticks(position),IsPaused=paused,CanSeek=canSeek,
        PlayMethod=PlaybackStartInfo_PlayMethod.DirectPlay,VolumeLevel=(int)(volume*100),IsMuted=volume==0
    },cancellationToken:ct));
    public void Progress(PlaybackSource s,double position,bool paused,double volume,bool canSeek)
    {
        lock(progressLock) {
            var slot=pendingProgress??new ProgressSlot();
            slot.Send=ct=>api.Sessions.Playing.Progress.PostAsync(new PlaybackProgressInfo {
        ItemId=s.ItemId,MediaSourceId=s.MediaSourceId,PlaySessionId=s.PlaySessionId,PositionTicks=Ticks(position),IsPaused=paused,CanSeek=canSeek,
        PlayMethod=PlaybackProgressInfo_PlayMethod.DirectPlay,VolumeLevel=(int)(volume*100),IsMuted=volume==0
            },cancellationToken:ct);
            if(pendingProgress is null) {pendingProgress=slot;reports.Writer.TryWrite(async ct=> {
                Func<CancellationToken,Task>? send;
                lock(progressLock){send=slot.Send;if(ReferenceEquals(pendingProgress,slot))pendingProgress=null;}
                if(send is not null)await send(ct).ConfigureAwait(false);
            });}
        }
    }
    public void Stop(PlaybackSource s,double position,bool failed)
    {
        // Discard obsolete progress while preserving stop/start ordering. Future
        // tracks must not replace that slot with progress before their Start.
        lock(progressLock) {if(pendingProgress is not null)pendingProgress.Send=null;pendingProgress=null;}
        reports.Writer.TryWrite(ct=>api.Sessions.Playing.Stopped.PostAsync(new PlaybackStopInfo {
        ItemId=s.ItemId,MediaSourceId=s.MediaSourceId,PlaySessionId=s.PlaySessionId,PositionTicks=Ticks(position),Failed=failed
        },cancellationToken:ct));
    }
    // Single FIFO as in Jellyfin MPV Shim: outgoing stop must precede incoming start.
    private async Task ReportLoop()
    {
        try {await foreach(var send in reports.Reader.ReadAllAsync(stopping.Token).ConfigureAwait(false)) {
            try {using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);timeout.CancelAfter(TimeSpan.FromSeconds(5));await send(timeout.Token).ConfigureAwait(false);}
            catch {ReportFailures++;}
        }}catch(OperationCanceledException){}
    }
    public async Task FlushAsync() {var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);if(reports.Writer.TryWrite(_=>{done.TrySetResult();return Task.CompletedTask;}))await done.Task.WaitAsync(TimeSpan.FromSeconds(10));}
    public async Task<bool> VerifyServerStateAsync(string? itemId,bool paused,double position)
    {
        await FlushAsync();
        var sessions=await api.Sessions.GetAsync(c=>c.QueryParameters.DeviceId=connection.DeviceId);
        var current=sessions?.FirstOrDefault(s=>s.DeviceId==connection.DeviceId);
        if(itemId is null)return current?.NowPlayingItem is null;
        return current?.NowPlayingItem?.Id==Guid.Parse(itemId)&&current.PlayState?.IsPaused==paused&&
            current.PlayState?.PlayMethod?.ToString()=="DirectPlay"&&Math.Abs((current.PlayState?.PositionTicks??0)/10000000d-position)<3;
    }
    public void Dispose() {reports.Writer.TryComplete();if(!reporter.Wait(TimeSpan.FromSeconds(5)))stopping.Cancel();_=reporter.ContinueWith(_=>{api.Dispose();stopping.Dispose();},TaskScheduler.Default);}
}

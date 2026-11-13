using System.IO;
using System.Text.Json;

namespace JellyfinMusic;
public static class SwitchTest
{
    public static async Task<int> RunAsync(string path)
    {
        var report=new Dictionary<string,object>(); string stage="login";
        using var client=new JellyfinClient(Environment.GetEnvironmentVariable("JFM_TEST_SERVER")!);
        try {
            await client.LoginAsync(Environment.GetEnvironmentVariable("JFM_TEST_USER")!,Environment.GetEnvironmentVariable("JFM_TEST_PASSWORD")!);
            stage="library"; var albums=await client.GetAlbumsAsync();
            var tracks=new List<Track>();
            foreach(var album in albums.Take(3)) tracks.AddRange((await client.GetTracksAsync(album)).Take(2));
            var session=new PlaybackSession();
            stage="playback"; using var audio=new AudioPlayback(client,session); audio.SetVolume(0);
            var steps=new List<object>(); bool passed=true;
            async Task Verify(string action) {
                for(int i=0;i<100 && session.IsPlaying && (!audio.HasOpened||session.Position<1);i++) await Task.Delay(200);
                bool ok=audio.HasOpened&&session.Position>=1&&session.IsPlaying;
                steps.Add(new { action,ok,opened=audio.HasOpened,position=session.Position,error=audio.LastFailureCode,detail=audio.LastFailureDetail }); passed&=ok;
            }
            session.Play(tracks); await Verify("initial");
            for(int i=1;i<tracks.Count;i++) { session.Select(i); await Verify("select-"+i); }
            session.Play(tracks,0); await Verify("replace-queue");
            for(int burst=0;burst<4;burst++) {
                for(int i=0;i<15;i++) { session.Select(i%tracks.Count); await Task.Delay(20+Random.Shared.Next(150)); }
                await Verify("rapid-selection-"+burst);
            }
            session.Pause(); session.Select(0); await Verify("paused-selection");
            session.Play([tracks[0] with { Id="00000000000000000000000000000000" }]);
            for(int i=0;i<150&&session.IsPlaying;i++) await Task.Delay(200);
            report["failedTrackStopped"]=!session.IsPlaying;
            session.Play(tracks,0); await Verify("recovery-after-failure");
            session.Pause();await Task.Delay(300);var pausedAt=session.Position;await Task.Delay(700);
            report["pauseStable"]=Math.Abs(session.Position-pausedAt)<.15;
            session.Seek(30);await Task.Delay(800);
            report["seekWhilePaused"]=Math.Abs(session.Position-30)<1&&!session.IsPlaying;
            report["serverPausedDirectPlay"]=await audio.VerifyServerStateAsync(session.Current!.Id,true,30);
            session.Toggle();await Task.Delay(1200);report["resumeAfterSeek"]=session.Position>30.5;
            session.Seek(60);await Task.Delay(1200);report["seekWhilePlaying"]=session.Position>60.5&&session.Position<64;
            session.Previous();await Task.Delay(500);report["previousSeeksToStart"]=session.Position<2;
            session.Loop=LoopMode.Track;var oldRevision=session.Revision;
            session.Seek(tracks[0].Seconds-.7);
            for(int i=0;i<80&&session.Revision==oldRevision;i++)await Task.Delay(100);
            await Verify("native-eof-track-repeat");
            report["nativeTrackRepeat"]=session.Revision>oldRevision&&session.Current?.Id==tracks[0].Id&&session.Loop==LoopMode.Track;
            session.RegisterAlbum(tracks.Where(t=>t.AlbumId==tracks[0].AlbumId).ToArray());session.Loop=LoopMode.Album;
            session.Play(tracks,0);await Verify("album-mode-after-selection");report["albumLoopRetained"]=session.Loop==LoopMode.Album;
            session.Pause();await Task.Delay(200);report["serverCurrentItemAfterSwitch"]=await audio.VerifyServerStateAsync(session.Current!.Id,true,session.Position);
            session.Clear();await audio.FlushReportsAsync();report["serverStopped"]=await audio.VerifyServerStateAsync(null,true,0);
            report["reportFailures"]=audio.ReportFailures;report["steps"]=steps;
            report["passed"]=passed&&report.Values.OfType<bool>().All(v=>v)&&audio.ReportFailures==0;
        } catch(Exception e) { report["passed"]=false; report["error"]=e.GetType().Name; report["stage"]=stage; report["detail"]=client.Redact(e.Message); }
        finally { await client.LogoutAsync(); }
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        return report["passed"] is true?0:1;
    }
}

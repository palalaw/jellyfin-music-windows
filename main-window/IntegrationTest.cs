using System.IO;
using System.Text.Json;

namespace JellyfinMusic;
public static class IntegrationTest
{
    public static async Task<int> VerifyLibrariesAsync(string reportPath)
    {
        var results=new Dictionary<string,object>();
        using var client=new JellyfinClient(Environment.GetEnvironmentVariable("JFM_TEST_SERVER")??throw new InvalidOperationException("Missing test environment"));
        try {
            await client.LoginAsync(Environment.GetEnvironmentVariable("JFM_TEST_USER")??"",Environment.GetEnvironmentVariable("JFM_TEST_PASSWORD")??"");
            var libraries=await client.GetLibrariesAsync(); var all=await client.GetAlbumsAsync();
            results["libraryCount"]=libraries.Count; results["allAlbumCount"]=all.Count;
            var allIds=all.Select(a=>a.Id).ToHashSet(); var scoped=new Dictionary<string,int>(); bool valid=true;
            for(int i=0;i<libraries.Count;i++) {
                var albums=await client.GetAlbumsAsync(libraries[i].Id);
                scoped[$"library-{i+1}"]=albums.Count; valid&=albums.All(a=>allIds.Contains(a.Id));
            }
            results["scopedAlbumCounts"]=scoped; results["scopedAlbumsBelongToAccessibleCollection"]=valid;
            results["passed"]=libraries.Count>0&&valid;
            await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented=true })); return libraries.Count>0&&valid?0:1;
        } catch(Exception e) { await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(new { passed=false,errorType=e.GetType().Name })); return 1; }
        finally { await client.LogoutAsync(); }
    }
    // Credentials are supplied only via this process's environment, never source files.
    public static async Task<int> RunAsync(string reportPath)
    {
        var results=new Dictionary<string,object>();
        using var client=new JellyfinClient(Environment.GetEnvironmentVariable("JFM_TEST_SERVER")??throw new InvalidOperationException("Missing test environment"));
        try {
            await client.LoginAsync(Environment.GetEnvironmentVariable("JFM_TEST_USER")??"",Environment.GetEnvironmentVariable("JFM_TEST_PASSWORD")??"");
            results["authentication"]=true;
            using(var resumed=new JellyfinClient(client.Profile.Address.AbsoluteUri)) {
                resumed.Restore(client.Export(""));
                results["tokenReconnect"]=(await resumed.GetLibrariesAsync()).Count>0;
            }
            var albums=await client.GetAlbumsAsync(); results["albumCount"]=albums.Count;
            var album=albums.First(); var tracks=await client.GetTracksAsync(album); results["sampleTrackCount"]=tracks.Count;
            results["artworkBytes"]=(await client.GetArtworkAsync(album)).Length;
            results["streamResponse"]=await client.ProbeStreamAsync(tracks[0]);
            bool original=album.IsFavorite;
            try {
                results["favoriteWrite"]=await client.SetFavoriteAsync(album.Id,!original)==!original;
                var refreshed=await client.GetAlbumsAsync(); results["favoriteRead"]=refreshed.Single(a=>a.Id==album.Id).IsFavorite==!original;
            } finally {
                results["favoriteRestored"]=await client.SetFavoriteAsync(album.Id,original)==original;
            }
            var session=new PlaybackSession(); session.RegisterAlbum(tracks);
            using var audio=new AudioPlayback(client,session); audio.SetVolume(0);
            session.Play(tracks);
            for(int i=0;i<60 && (!audio.HasOpened||session.Position<2);i++) await Task.Delay(500);
            results["mediaOpened"]=audio.HasOpened; results["positionAdvanced"]=session.Position>=2; results["serverTranscode"]=audio.IsTranscoding; results["mediaErrorCode"]=audio.LastFailureCode??"none";
            results["mediaErrorDetail"]=audio.LastFailureDetail??"none";
            session.Pause(); var position=session.Position; await Task.Delay(1000); results["paused"]=Math.Abs(position-session.Position)<.2;
            session.Seek(10); session.Toggle(); await Task.Delay(2000);
            results["seekAdvanced"]=audio.HasOpened && session.Position>10.5;
            session.Loop=LoopMode.Track; session.Next(); results["loopRetainedAfterNext"]=session.Loop==LoopMode.Track;
            session.Loop=LoopMode.Album; session.Play(tracks,tracks.Count-1); session.Ended(); results["albumWrapped"]=session.Current==tracks[0]&&session.Loop==LoopMode.Album;
            session.Pause();
            bool pass=results.Where(k=>k.Key!="serverTranscode").All(k=>k.Value is not bool b||b);
            results["passed"]=pass;
            await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented=true })); return pass?0:1;
        } catch(Exception e) {
            results["passed"]=false; results["errorType"]=e.GetType().Name; // Never serialize URLs, tokens or server responses.
            await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented=true })); return 1;
        } finally { await client.LogoutAsync(); }
    }
}

namespace JellyfinMusic;
public static class SelfTest
{
    public static int Run()
    {
        static void Check(bool value,string reason) { if(!value) throw new InvalidOperationException(reason); }
        var library=new DemoLibrary(); var a=library.Albums[0].Tracks; var b=library.Albums[1].Tracks;
        var p=new PlaybackSession(); p.RegisterAlbum(a); p.RegisterAlbum(b);
        p.Toggle(); p.Next(); p.Previous(); Check(p.Current is null,"Empty queue");
        p.Play(a,2); p.Seek(99999); Check(p.Position==a[2].Seconds,"Seek clamping");
        p.Previous(); Check(p.Index==2&&p.Position==0,"Previous restarts"); p.Previous(); Check(p.Index==1,"Previous track");
        p.Remove(0); Check(p.Current==a[1],"Removing earlier queue item");
        p.Play(a,a.Count-1); p.Ended(); Check(!p.IsPlaying,"Off stops at queue end");
        p.Loop=LoopMode.Track; p.Play(a,1); var revision=p.Revision; p.Ended();
        Check(p.Current==a[1]&&p.Revision>revision&&p.IsPlaying,"Single track loop at natural end");
        p.Next(); Check(p.Current==a[2]&&p.Loop==LoopMode.Track,"Manual next skips while preserving track loop");
        p.Play(b,3); Check(p.Loop==LoopMode.Track,"New album preserves track loop"); p.Ended(); Check(p.Current==b[3],"New selected track loops");
        p.Loop=LoopMode.Album; p.Play(a.Concat(b),a.Count-1); p.Ended(); Check(p.Current==a[0],"Album loop never spills into another queued album");
        p.Select(a.Count+1); p.Ended(); Check(p.Current==b[2]&&p.Loop==LoopMode.Album,"Selected album becomes loop scope");
        p.Play([a[3]]); p.Ended(); Check(p.Current==a[4],"Album loop includes full registered album for partial queue");
        p.Ended(); Check(p.Current==a[0],"Album loop wraps full album");
        p.Play(b,0); p.Previous(); Check(p.Current==b[^1]&&p.Loop==LoopMode.Album,"Previous wraps selected album");
        p.Shuffle=true; p.Next(); Check(p.Current!.AlbumId==b[0].AlbumId,"Shuffle stays within album loop");
        p.Loop=LoopMode.Track; var current=p.Current; p.Ended(); Check(p.Current==current,"Track loop precedes shuffle at natural end");
        p.Play(p.Queue); Check(p.Queue.Count>0,"Self-queue replacement");
        p.Pause(); var pos=p.Position; p.Tick(5); Check(p.Position==pos,"Paused clock");
        while(p.Queue.Count>0) p.Remove(0); Check(p.Current is null&&!p.IsPlaying&&p.Loop==LoopMode.Track,"Empty queue keeps loop preference");
        var history=new NavigationHistory();
        var state=new NavigationState("favorites",null,"query","library-A",1234);
        history.Push(state); history.Push(state); Check(history.Pop()==state&&!history.CanGoBack,"History deduplicates and restores search/library/scroll");
        history.Push(state); history.Clear(); Check(history.Pop() is null,"Connection reset clears history");
        Check(BackInput.IsBack(System.Windows.Input.Key.BrowserBack,System.Windows.Input.Key.None,System.Windows.Input.ModifierKeys.None),"BrowserBack key");
        Check(BackInput.IsBack(System.Windows.Input.Key.System,System.Windows.Input.Key.Left,System.Windows.Input.ModifierKeys.Alt),"Alt Left");
        Check(!BackInput.IsBack(System.Windows.Input.Key.Back,System.Windows.Input.Key.None,System.Windows.Input.ModifierKeys.None),"Backspace remains text editing");
        Check(BackInput.IsBackAppCommand((nint)(1<<16))&&!BackInput.IsBackAppCommand((nint)(2<<16)),"Only browser-back app command");
        using(var client=new JellyfinClient("https://example.invalid/")) {
            var uri=client.StreamUri(a[0]);
            Check(uri.Query.Contains("Static=true")&&!uri.Query.Contains("AudioCodec")&&!uri.Query.Contains("BitRate"),"Original stream has no encoding settings");
        }
        var root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"JellyfinMusic-test-"+Guid.NewGuid().ToString("N"));
        var store=new LocalStore(root);
        var saved=new SavedConnection("https://example.invalid/","sample-user","sample-id","secret-test-token","device-test");
        store.Save(saved);
        Check(new LocalStore(root).Load()==saved,"Encrypted connection reload");
        Check(!System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(System.IO.Path.Combine(root,"connection.bin"))).Contains(saved.Token),"Token is not plaintext");
        var key=store.ArtworkKey(saved.Address,saved.UserId,library.Albums[0]);
        Check(key!=store.ArtworkKey("https://other.invalid/",saved.UserId,library.Albums[0]),"Artwork server isolation");
        Check(key!=store.ArtworkKey(saved.Address,saved.UserId,library.Albums[0] with { ImageTag="updated" }),"Artwork tag invalidation");
        store.WriteArtwork(key,[1,2,3]);
        Check(new LocalStore(root).ReadArtwork(key)!.SequenceEqual(new byte[]{1,2,3}),"Artwork survives store restart");
        store.ClearArtwork(); store.Forget();
        Check(store.ReadArtwork(key) is null&&store.Load() is null,"Clear saved data");
        System.IO.Directory.Delete(System.IO.Path.Combine(root,"artwork")); System.IO.Directory.Delete(root);
        return 0;
    }
}

namespace JellyfinMusic;

public enum LoopMode { Off, Album, Track }

// No UI or audio-engine dependencies. Loop mode is never reset by selection.
public sealed class PlaybackSession
{
    private readonly List<Track> queue = [];
    private readonly Dictionary<string, IReadOnlyList<Track>> albums = [];
    public IReadOnlyList<Track> Queue => queue;
    public int Index { get; private set; } = -1;
    public Track? Current => Index >= 0 && Index < queue.Count ? queue[Index] : null;
    public bool IsPlaying { get; private set; }
    public double Position { get; private set; }
    public bool Shuffle { get; set; }
    public LoopMode Loop { get; set; }
    public long Revision { get; private set; }
    public event Action? Changed;
    public event Action<double>? SeekRequested;
    private static string AlbumKey(Track t) => string.IsNullOrEmpty(t.AlbumId) ? t.Artist+"\0"+t.Album : t.AlbumId;
    public void RegisterAlbum(IReadOnlyList<Track> tracks) { if(tracks.Count>0) albums[AlbumKey(tracks[0])]=tracks.ToArray(); }
    public void Play(IEnumerable<Track> tracks,int index=0)
    {
        var replacement=tracks.ToArray();
        if(replacement.Length==0 || index<0 || index>=replacement.Length) return;
        queue.Clear(); queue.AddRange(replacement); Select(index);
    }
    public void Toggle() { if(Current is null) return; IsPlaying=!IsPlaying; Changed?.Invoke(); }
    public void Pause() { IsPlaying=false; Changed?.Invoke(); }
    public void Seek(double seconds) { Position=Math.Clamp(seconds,0,Current?.Seconds ?? 0); SeekRequested?.Invoke(Position); Changed?.Invoke(); }
    public void UpdatePosition(double seconds) { Position=Math.Clamp(seconds,0,Current?.Seconds ?? 0); Changed?.Invoke(); }
    public void Select(int index) { if(index<0 || index>=queue.Count) return; Index=index; Position=0; IsPlaying=true; Revision++; Changed?.Invoke(); }
    private void AdvanceAlbum(int direction)
    {
        if(Current is not { } current) return;
        var members=albums.TryGetValue(AlbumKey(current),out var all) ? all : queue.Where(t=>AlbumKey(t)==AlbumKey(current)).ToArray();
        var at=members.ToList().FindIndex(t=>t.Id==current.Id);
        if(at<0 || members.Count==0) return;
        var offset=Shuffle && direction>0 && members.Count>1 ? Random.Shared.Next(1,members.Count) : direction;
        var target=members[(at+offset+members.Count)%members.Count];
        var index=queue.FindIndex(t=>t.Id==target.Id);
        if(index<0) { queue.Insert(Index+1,target); index=Index+1; }
        Select(index);
    }
    public void Previous()
    {
        if(Current is null) return;
        if(Position>3) Seek(0);
        else if(Loop==LoopMode.Album) AdvanceAlbum(-1);
        else Select(Math.Max(0,Index-1));
    }
    // Manual next skips even when Track loop is enabled.
    public void Next()
    {
        if(Current is null) return;
        if(Loop==LoopMode.Album) { AdvanceAlbum(1); return; }
        if(Shuffle && queue.Count>1) { Select((Index+Random.Shared.Next(1,queue.Count))%queue.Count); return; }
        if(Index+1<queue.Count) Select(Index+1);
        else { IsPlaying=false; Position=Current.Seconds; Changed?.Invoke(); }
    }
    public void Ended() { if(Current is null) return; if(Loop==LoopMode.Track) Select(Index); else Next(); }
    public void Tick(double seconds) { if(!IsPlaying || Current is null) return; Position+=seconds; if(Position>=Current.Seconds) Ended(); else Changed?.Invoke(); }
    public void Enqueue(IEnumerable<Track> tracks) { queue.AddRange(tracks.ToArray()); Changed?.Invoke(); }
    public void Remove(int index)
    {
        if(index<0 || index>=queue.Count) return;
        queue.RemoveAt(index);
        if(queue.Count==0) { Index=-1; Position=0; IsPlaying=false; Revision++; }
        else if(index<Index) Index--;
        else if(index==Index) { Index=Math.Min(Index,queue.Count-1); Position=0; Revision++; }
        Changed?.Invoke();
    }
    public void Clear() { queue.Clear(); albums.Clear(); Index=-1; IsPlaying=false; Position=0; Revision++; Changed?.Invoke(); }
}

namespace JellyfinMusic;

// Engine reports observations; queue/loop decisions belong to PlaybackSession.
public sealed record AudioObservation(long Generation,bool Loaded,double Position,double Duration,bool Paused,bool Seeking,bool Buffering,bool CanSeek);
public interface IAudioEngine : IDisposable
{
    event Action<AudioObservation>? Updated;
    event Action<long>? Ended;
    event Action<long,string>? Failed;
    void Load(Uri uri,string authorization,long generation,double position,bool paused);
    void Stop();
    void SetPaused(bool paused);
    void Seek(double seconds);
    void SetVolume(double volume);
    void Poll();
}

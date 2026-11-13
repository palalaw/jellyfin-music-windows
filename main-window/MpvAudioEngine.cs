using System.Globalization;
using System.Runtime.InteropServices;

namespace JellyfinMusic;

// Thin binding to libmpv's stable C client API. No custom transport/decoder.
// Calls are confined to the owning dispatcher. libmpv performs I/O off-thread.
public sealed class MpvAudioEngine : IAudioEngine
{
    private IntPtr handle;
    private long generation,expectedEntry=-1,activeEntry=-1;
    private bool loaded;
    public event Action<AudioObservation>? Updated;
    public event Action<long>? Ended;
    public event Action<long,string>? Failed;
    public MpvAudioEngine()
    {
        handle=Native.mpv_create();
        if(handle==IntPtr.Zero) throw new InvalidOperationException("libmpvを初期化できません。");
        try {
            foreach(var (name,value) in new[] {("config","no"),("terminal","no"),("msg-level","all=no"),("video","no"),("audio-display","no"),("idle","yes"),("keep-open","no"),("tls-verify","yes"),("cache","yes"),("cache-on-disk","no"),("network-timeout","15")})
                Check(Native.mpv_set_option_string(handle,name,value));
            // Script-free builds omit these options. If supported, explicitly disable them.
            foreach(var option in new[] {"load-scripts","ytdl"}) {
                var result=Native.mpv_set_option_string(handle,option,"no");
                if(result!=-5)Check(result); // MPV_ERROR_OPTION_NOT_FOUND
            }
            Check(Native.mpv_initialize(handle));
        } catch { Native.mpv_terminate_destroy(handle);handle=IntPtr.Zero;throw; }
    }
    public void Load(Uri uri,string authorization,long version,double position,bool paused)
    {
        // Header is passed in-process, never in a URL, command line or log.
        // Escaping uses mpv's string-list syntax (not shell escaping).
        generation=version; loaded=false; expectedEntry=-1;
        Set("http-header-fields","Authorization: "+authorization.Replace("\\","\\\\").Replace(",","\\,"));
        SetPaused(paused);
        Command("loadfile",uri.AbsoluteUri,"replace","-1","start="+position.ToString("R",CultureInfo.InvariantCulture));
        expectedEntry=Int64("playlist/0/id")??throw new InvalidOperationException("libmpvの再生項目を取得できません。");
    }
    public void Stop() { if(handle==IntPtr.Zero)return; loaded=false;expectedEntry=-1;Command("stop"); }
    public void SetPaused(bool paused)=>Set("pause",paused?"yes":"no");
    public void Seek(double seconds)=>Command("seek",Math.Max(0,seconds).ToString("R",CultureInfo.InvariantCulture),"absolute+exact");
    public void SetVolume(double volume)=>Set("volume",(Math.Clamp(volume,0,1)*100).ToString("R",CultureInfo.InvariantCulture));
    public void Poll()
    {
        if(handle==IntPtr.Zero)return;
        for(int i=0;i<256;i++) {
            var e=Marshal.PtrToStructure<MpvEvent>(Native.mpv_wait_event(handle,0));
            if(e.Id==0)break;
            if(e.Id==6) activeEntry=Marshal.ReadInt64(e.Data); // START_FILE
            if(e.Id==8 && activeEntry==expectedEntry) loaded=true; // FILE_LOADED
            if(e.Id==7) { // END_FILE: STOP from replacement is not EOF.
                var end=Marshal.PtrToStructure<MpvEnd>(e.Data);
                if(end.Entry!=expectedEntry)continue;
                loaded=false;expectedEntry=-1;
                if(end.Reason==0) Ended?.Invoke(generation);
                else if(end.Reason==4) Failed?.Invoke(generation,"libmpv:"+end.Error);
            }
        }
        if(loaded && activeEntry==expectedEntry) Updated?.Invoke(new(generation,true,Double("time-pos")??0,Double("duration")??0,Flag("pause"),Flag("seeking"),Flag("paused-for-cache"),Flag("seekable")));
    }
    private void Set(string name,string value)=>Check(Native.mpv_set_property_string(handle,name,value));
    private double? Double(string name) { var r=Native.mpv_get_double(handle,name,5,out var v);return r>=0&&double.IsFinite(v)?v:null; }
    private long? Int64(string name)=>Native.mpv_get_int64(handle,name,4,out var v)>=0?v:null;
    private bool Flag(string name)=>Native.mpv_get_flag(handle,name,3,out var v)>=0&&v!=0;
    private static void Check(int code) { if(code<0)throw new InvalidOperationException("libmpv:"+code); }
    private void Command(params string[] args)
    {
        var strings=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        var pointers=Marshal.AllocHGlobal((strings.Length+1)*IntPtr.Size);
        try { for(int i=0;i<strings.Length;i++)Marshal.WriteIntPtr(pointers,i*IntPtr.Size,strings[i]);Marshal.WriteIntPtr(pointers,strings.Length*IntPtr.Size,IntPtr.Zero);Check(Native.mpv_command(handle,pointers)); }
        finally {Marshal.FreeHGlobal(pointers);foreach(var p in strings)Marshal.FreeCoTaskMem(p);}
    }
    public void Dispose() { if(handle==IntPtr.Zero)return;var old=handle;handle=IntPtr.Zero;Native.mpv_terminate_destroy(old); }
    [StructLayout(LayoutKind.Sequential)] private struct MpvEvent { public int Id,Error;public ulong Reply;public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] private struct MpvEnd { public int Reason,Error;public long Entry,InsertId;public int InsertCount; }
    private static class Native
    {
        private const string Dll="libmpv-2.dll";
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr mpv_create();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int mpv_initialize(IntPtr h);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void mpv_terminate_destroy(IntPtr h);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr mpv_wait_event(IntPtr h,double timeout);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int mpv_command(IntPtr h,IntPtr args);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int mpv_set_option_string(IntPtr h,[MarshalAs(UnmanagedType.LPUTF8Str)]string n,[MarshalAs(UnmanagedType.LPUTF8Str)]string v);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int mpv_set_property_string(IntPtr h,[MarshalAs(UnmanagedType.LPUTF8Str)]string n,[MarshalAs(UnmanagedType.LPUTF8Str)]string v);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,EntryPoint="mpv_get_property")] internal static extern int mpv_get_double(IntPtr h,[MarshalAs(UnmanagedType.LPUTF8Str)]string n,int format,out double v);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,EntryPoint="mpv_get_property")] internal static extern int mpv_get_int64(IntPtr h,[MarshalAs(UnmanagedType.LPUTF8Str)]string n,int format,out long v);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,EntryPoint="mpv_get_property")] internal static extern int mpv_get_flag(IntPtr h,[MarshalAs(UnmanagedType.LPUTF8Str)]string n,int format,out int v);
    }
}



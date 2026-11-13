using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace JellyfinMusic;

public sealed record ServerProfile(Guid Id, Uri Address);
public sealed record MusicLibrary(string Id,string Name);

public sealed class JellyfinClient : IDisposable
{
    private readonly HttpClient http;
    private string deviceId=Guid.NewGuid().ToString("N");
    private string token="";
    private string userId="";
    public ServerProfile Profile { get; }
    public SavedConnection Export(string username)=>new(Profile.Address.AbsoluteUri,username,userId,token,deviceId);
    public void Restore(SavedConnection saved) { token=saved.Token; userId=saved.UserId; deviceId=saved.DeviceId; SetAuthorization(); }
    public JellyfinClient(string address)
    {
        if(!Uri.TryCreate(address.Trim().TrimEnd('/')+"/",UriKind.Absolute,out var uri) ||
           (uri.Scheme!="https" && uri.Scheme!="http") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("サーバーURLを確認してください。");
        Profile=new(Guid.NewGuid(),uri);
        http=new(new HttpClientHandler { AllowAutoRedirect=false,UseDefaultCredentials=false }) { BaseAddress=uri, Timeout=TimeSpan.FromSeconds(30) };
        SetAuthorization();
    }
    private void SetAuthorization()
    {
        http.DefaultRequestHeaders.Remove("Authorization");
        var value=$"MediaBrowser Client=\"JellyfinMusic\", Device=\"Windows\", DeviceId=\"{deviceId}\", Version=\"0.5.2-preview.1\"";
        if(token.Length>0) value+= $", Token=\"{token}\"";
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",value);
    }
    private static void Check(HttpResponseMessage response)
    {
        if(!response.IsSuccessStatusCode) throw new HttpRequestException($"Jellyfinへの要求が失敗しました（HTTP {(int)response.StatusCode}）。");
    }
    public async Task LoginAsync(string username,string password,CancellationToken ct=default)
    {
        using var response=await http.PostAsJsonAsync("Users/AuthenticateByName",new { Username=username,Pw=password },ct);
        Check(response); using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        token=json.RootElement.GetProperty("AccessToken").GetString()!;
        userId=json.RootElement.GetProperty("User").GetProperty("Id").GetString()!; SetAuthorization();
    }
    private static string Text(JsonElement e,string key,string fallback="") => e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String ? v.GetString()??fallback : fallback;
    private static int Number(JsonElement e,string key) => e.TryGetProperty(key,out var v)&&v.TryGetInt32(out var n)?n:0;
    private static string Artist(JsonElement e) => Text(e,"AlbumArtist",e.TryGetProperty("Artists",out var a)&&a.GetArrayLength()>0?a[0].GetString()??"":"");
    public async Task<IReadOnlyList<MusicLibrary>> GetLibrariesAsync(CancellationToken ct=default)
    {
        using var response=await http.GetAsync($"Users/{userId}/Views",ct); Check(response);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return json.RootElement.GetProperty("Items").EnumerateArray()
            .Where(item=>Text(item,"CollectionType")=="music")
            .Select(item=>new MusicLibrary(Text(item,"Id"),Text(item,"Name"))).ToArray();
    }
    public async Task<IReadOnlyList<Album>> GetAlbumsAsync(string? libraryId=null,CancellationToken ct=default)
    {
        var result=new List<Album>();
        for(int start=0;;start+=200) {
            var scope=string.IsNullOrEmpty(libraryId)?"":$"&ParentId={Uri.EscapeDataString(libraryId)}";
            using var response=await http.GetAsync($"Items?UserId={userId}&IncludeItemTypes=MusicAlbum&Recursive=true&Fields=Genres&EnableUserData=true&SortBy=SortName&StartIndex={start}&Limit=200{scope}",ct);
            Check(response); using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var items=json.RootElement.GetProperty("Items");
            foreach(var item in items.EnumerateArray()) {
                var favorite=item.TryGetProperty("UserData",out var data)&&data.TryGetProperty("IsFavorite",out var f)&&f.GetBoolean();
                var genre=item.TryGetProperty("Genres",out var genres)&&genres.GetArrayLength()>0?genres[0].GetString()??"":"";
                var tag=item.TryGetProperty("ImageTags",out var tags)?Text(tags,"Primary"):"";
                result.Add(new(Text(item,"Id"),Text(item,"Name"),Artist(item),Number(item,"ProductionYear"),genre,"#547B76","♪",[],favorite,tag));
            }
            if(items.GetArrayLength()==0 || result.Count>=Number(json.RootElement,"TotalRecordCount")) break;
        }
        return result;
    }
    public async Task<IReadOnlyList<Track>> GetTracksAsync(Album album,CancellationToken ct=default)
    {
        var result=new List<Track>();
        for(int start=0;;start+=200) {
            using var response=await http.GetAsync($"Items?UserId={userId}&ParentId={Uri.EscapeDataString(album.Id)}&IncludeItemTypes=Audio&Recursive=true&SortBy=ParentIndexNumber,IndexNumber,SortName&StartIndex={start}&Limit=200",ct);
            Check(response); using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct)); var items=json.RootElement.GetProperty("Items");
            foreach(var item in items.EnumerateArray()) {
                var seconds=item.TryGetProperty("RunTimeTicks",out var ticks)?(int)(ticks.GetInt64()/TimeSpan.TicksPerSecond):0;
                result.Add(new(Text(item,"Id"),Text(item,"Name"),Artist(item),album.Title,seconds,album.Color,album.Id,Text(item,"Container","flac")));
            }
            if(items.GetArrayLength()==0 || result.Count>=Number(json.RootElement,"TotalRecordCount")) break;
        }
        return result;
    }
    public async Task<bool> SetFavoriteAsync(string albumId,bool favorite,CancellationToken ct=default)
    {
        using var request=new HttpRequestMessage(favorite?HttpMethod.Post:HttpMethod.Delete,$"Users/{userId}/FavoriteItems/{Uri.EscapeDataString(albumId)}");
        using var response=await http.SendAsync(request,ct); Check(response);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return json.RootElement.GetProperty("IsFavorite").GetBoolean();
    }
    public async Task<byte[]> GetArtworkAsync(Album album,CancellationToken ct=default)
    {
        using var response=await http.GetAsync($"Items/{Uri.EscapeDataString(album.Id)}/Images/Primary?MaxWidth=360&Quality=85&Tag={Uri.EscapeDataString(album.ImageTag??"")}",ct);
        Check(response); return await response.Content.ReadAsByteArrayAsync(ct);
    }
    public Uri StreamUri(Track track) => new(Profile.Address,$"Audio/{Uri.EscapeDataString(track.Id)}/stream?Static=true&DeviceId={deviceId}&api_key={Uri.EscapeDataString(token)}");
    public async Task<HttpResponseMessage> OpenStreamAsync(Track track,bool head,string? range,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(head?HttpMethod.Head:HttpMethod.Get,
            $"Audio/{Uri.EscapeDataString(track.Id)}/stream?Static=true&DeviceId={deviceId}");
        if(range is not null) request.Headers.Range=System.Net.Http.Headers.RangeHeaderValue.Parse(range);
        return await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    }
    public async Task LogoutAsync()
    {
        if(token.Length==0) return;
        try { using var response=await http.PostAsync("Sessions/Logout",null); }
        catch(HttpRequestException) { } catch(TaskCanceledException) { }
        finally { token=""; userId=""; SetAuthorization(); }
    }
    public async Task<string> ProbeStreamAsync(Track track)
    {
        // Separate no-header client checks the exact URI used by MediaPlayer.
        using var probe=new HttpClient { Timeout=TimeSpan.FromSeconds(20) };
        using var request=new HttpRequestMessage(HttpMethod.Get,StreamUri(track));
        request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(0,1023);
        using var response=await probe.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);
        using var stream=await response.Content.ReadAsStreamAsync();
        var buffer=new byte[16]; var count=await stream.ReadAsync(buffer);
        return $"{(int)response.StatusCode}; {response.Content.Headers.ContentType?.MediaType}; bytes={count}";
    }
    public void Dispose() { token=""; http.Dispose(); }
    public string Redact(string message) => System.Text.RegularExpressions.Regex.Replace(token.Length>0?message.Replace(token,"[token]"):message,@"https?://\S+","[url]");
}

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JellyfinMusic;

public sealed record SavedConnection(string Address,string Username,string UserId,string Token,string DeviceId);
public sealed class LocalStore
{
    public string Root { get; }
    public LocalStore(string? root=null) => Root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JellyfinMusic");
    private string ConnectionPath=>Path.Combine(Root,"connection.bin");
    public SavedConnection? Load()
    {
        if(!File.Exists(ConnectionPath)) return null;
        var clear=ProtectedData.Unprotect(File.ReadAllBytes(ConnectionPath),null,DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<SavedConnection>(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(SavedConnection connection)
    {
        var clear=JsonSerializer.SerializeToUtf8Bytes(connection);
        try { AtomicWrite(ConnectionPath,ProtectedData.Protect(clear,null,DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Forget() { if(File.Exists(ConnectionPath)) File.Delete(ConnectionPath); }
    public string ArtworkKey(string address,string userId,Album album)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{address}\n{userId}\n{album.Id}\n{album.ImageTag}\n360-85")));
    public byte[]? ReadArtwork(string key)
    {
        try { var path=Path.Combine(Root,"artwork",key+".img"); if(!File.Exists(path)) return null; File.SetLastWriteTimeUtc(path,DateTime.UtcNow); return File.ReadAllBytes(path); }
        catch(IOException) { return null; } catch(UnauthorizedAccessException) { return null; }
    }
    public void WriteArtwork(string key,byte[] bytes)
    {
        try {
            if(bytes.Length>16*1024*1024) return;
            AtomicWrite(Path.Combine(Root,"artwork",key+".img"),bytes);
            var files=new DirectoryInfo(Path.Combine(Root,"artwork")).GetFiles("*.img").OrderBy(f=>f.LastWriteTimeUtc).ToArray();
            long total=files.Sum(f=>f.Length);
            foreach(var file in files) { if(total<=256L*1024*1024) break; total-=file.Length; file.Delete(); }
        } catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
    public void ClearArtwork()
    {
        var directory=Path.Combine(Root,"artwork");
        if(Directory.Exists(directory)) foreach(var file in Directory.EnumerateFiles(directory,"*.img")) File.Delete(file);
    }
    private static void AtomicWrite(string path,byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temp,bytes); File.Move(temp,path,true); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
}

using System.IO;
using System.Text.Json;
namespace JellyfinMusic;
public static class PlayerPreferences
{
    private sealed record Values(bool TaskbarVisible);
    private static string PathName=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JellyfinMusic","preferences.json");
    public static bool LoadTaskbar(bool initial)
    {
        try {return File.Exists(PathName)?JsonSerializer.Deserialize<Values>(File.ReadAllText(PathName))?.TaskbarVisible??initial:initial;}
        catch {return false;}
    }
    public static void SaveTaskbar(bool visible)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temp=PathName+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,JsonSerializer.Serialize(new Values(visible)));File.Move(temp,PathName,true);}
        finally {if(File.Exists(temp)) File.Delete(temp);}
    }
}

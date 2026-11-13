namespace JellyfinMusic;

public record Track(string Id, string Title, string Artist, string Album, int Seconds, string Color, string AlbumId = "", string Container = "flac")
{
    public string Duration => TimeSpan.FromSeconds(Seconds).ToString(@"m\:ss");
}
public record Album(string Id, string Title, string Artist, int Year, string Genre, string Color, string Mark, IReadOnlyList<Track> Tracks, bool IsFavorite = false, string? ImageTag = null);

// Replace this provider with a Jellyfin-backed implementation; UI never owns credentials.
public interface ILibraryProvider { IReadOnlyList<Album> Albums { get; } }
public sealed class DemoLibrary : ILibraryProvider
{
    public IReadOnlyList<Album> Albums { get; } = Build();
    private static IReadOnlyList<Album> Build()
    {
        var records = new[] {
            ("Midnight Signals", "Sora Ensemble", 2025, "Electronic", "#547B91", "01", new[]{"Blue hour", "夜の輪郭", "Signals", "After the rain", "帰り道"}),
            ("木漏れ日の記憶", "青葉カルテット", 2024, "Jazz", "#819273", "02", new[]{"ひかりの庭", "午後の窓辺", "遠い声", "木漏れ日", "余韻"}),
            ("Tidal Patterns", "Nami", 2025, "Ambient", "#648B9E", "03", new[]{"Low tide", "Glass ocean", "Drift", "Waterline", "Still blue"}),
            ("Soft Geometry", "Mellow Forms", 2023, "Electronic", "#B48771", "04", new[]{"Circles", "Soft focus", "Parallel", "Warm lines", "Fold"}),
            ("街と余白", "Sora Ensemble", 2024, "Jazz", "#9B807F", "05", new[]{"始発", "交差点", "余白", "まどろみ", "街灯"}),
            ("A Place to Return", "Northbound", 2025, "Acoustic", "#988D68", "06", new[]{"Open road", "Homeward", "Wild grass", "A quiet room", "Return"}),
            ("Moon Garden", "Nami", 2022, "Ambient", "#807B9D", "07", new[]{"Moonrise", "Petals", "Silver", "Night garden", "Dawn"}),
            ("日々のかけら", "青葉カルテット", 2025, "Jazz", "#A17A64", "08", new[]{"朝の珈琲", "小さな旅", "雨宿り", "日々", "また明日"})
        };
        return records.Select((r,i) => new Album($"demo-{i}", r.Item1,r.Item2,r.Item3,r.Item4,r.Item5,r.Item6,
            r.Item7.Select((t,n)=>new Track($"demo-{i}-{n}",t,r.Item2,r.Item1,183+n*29,r.Item5,$"demo-{i}")).ToArray())).ToArray();
    }
}



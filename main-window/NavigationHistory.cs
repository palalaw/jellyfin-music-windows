using System.Windows.Input;

namespace JellyfinMusic;

public sealed record NavigationState(string View,string? AlbumId,string Query,string LibraryId,double ScrollOffset);
public sealed class NavigationHistory
{
    private readonly List<NavigationState> previous=[];
    public bool CanGoBack=>previous.Count>0;
    public void Push(NavigationState state)
    {
        if(previous.LastOrDefault()==state) return;
        previous.Add(state); if(previous.Count>100) previous.RemoveAt(0);
    }
    public NavigationState? Pop() { if(previous.Count==0) return null; var state=previous[^1]; previous.RemoveAt(previous.Count-1); return state; }
    public void Clear()=>previous.Clear();
}
public static class BackInput
{
    public static bool IsBack(Key key,Key systemKey,ModifierKeys modifiers)=>key==Key.BrowserBack ||
        ((key==Key.System?systemKey:key)==Key.Left && modifiers==ModifierKeys.Alt);
    public static bool IsBackAppCommand(nint lParam)=>((lParam.ToInt64()>>16)&0xFFF)==1;
}

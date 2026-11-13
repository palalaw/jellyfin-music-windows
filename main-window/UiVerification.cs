using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace JellyfinMusic;
public partial class MainWindow
{
    public async Task<Dictionary<string,object>> VerifyNavigationAsync()
    {
        var result=new Dictionary<string,object>();
        async Task Layout()
        {
            var root=(FrameworkElement)Content;
            for(int i=0;i<4;i++) {
                root.Measure(new Size(1280,820)); root.Arrange(new Rect(0,0,1280,820)); root.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            }
        }
        timer.Stop(); LoadDemo(); var sample=albums.ToArray();
        albums=Enumerable.Range(0,480).Select(i=>sample[i%sample.Length] with { Id=$"ui-{i}" }).ToList();
        libraryCache[""]=albums; libraryCache["A"]=albums.Take(160).ToList(); libraryCache["B"]=albums.Skip(160).Take(80).ToList();
        SetLibraryOptions([new("A","Library A"),new("B","Library B")]); RenderPage(); await Layout();
        var browser=(AlbumBrowser)AlbumHost.Content;
        result["allAlbumsInScrollExtent"]=browser.AlbumCount==480;
        browser.ScrollToEnd(); await Layout();
        result["lastAlbumReachable"]=browser.LastRowRealized;
        result["virtualized"]=browser.RealizedRows<browser.Items.Count/2;
        result["realizedRows"]=browser.RealizedRows;
        result["totalRows"]=browser.Items.Count;
        double allOffset=browser.Offset;
        Navigate("favorites"); await Layout();
        NavigationCommands.BrowseBack.Execute(null,this); await Layout();
        result["commandRestoresScroll"]=view=="albums"&&Math.Abs(CurrentOffset-allOffset)<3;
        await SwitchLibraryAsync("A"); await Layout();
        result["librarySelection"]=libraryId=="A"&&albums.Count==160;
        Search.Text="Midnight"; await Layout();
        ((AlbumBrowser)AlbumHost.Content).ScrollToEnd(); await Layout(); double searchedOffset=CurrentOffset;
        await OpenAlbumAsync(albums[0]); await Layout(); NavigationCommands.BrowseBack.Execute(null,this); await Layout();
        result["detailBackRestoresSearchAndScroll"]=selectedAlbum is null&&Search.Text=="Midnight"&&libraryId=="A"&&Math.Abs(CurrentOffset-searchedOffset)<3;
        await OpenAlbumAsync(albums[0]); await Layout();
        RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.XButton1) { RoutedEvent=Mouse.PreviewMouseUpEvent }); await Layout();
        result["mouseBackEvent"]=selectedAlbum is null&&Search.Text=="Midnight";
        await SwitchLibraryAsync("B"); await Layout(); GoBack(); await Layout();
        result["backRestoresLibrary"]=libraryId=="A"&&albums.Count==160&&Search.Text=="Midnight";
        await DisconnectAsync(); await Layout(); result["disconnectClearsHistory"]=!history.CanGoBack;
        result["passed"]=result.Values.OfType<bool>().All(v=>v); return result;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace JellyfinMusic;

public sealed record AlbumRow(IReadOnlyList<Album> Albums,Func<Album,FrameworkElement> BuildCard);
public sealed class AlbumRowView : StackPanel
{
    public AlbumRowView()
    {
        Orientation=Orientation.Horizontal; Height=276;
        DataContextChanged+=(_,_)=> { Children.Clear(); if(DataContext is AlbumRow row) foreach(var album in row.Albums) Children.Add(row.BuildCard(album)); };
    }
}

// All albums are in the scroll extent. WPF recycles rows outside the viewport.
public sealed class AlbumBrowser : ListBox
{
    private readonly IReadOnlyList<Album> albums;
    private readonly Func<Album,FrameworkElement> build;
    private int columns;
    private double? pendingOffset;
    public int AlbumCount=>albums.Count;
    public double Offset=>FindScroll(this)?.VerticalOffset??0;
    public AlbumBrowser(IReadOnlyList<Album> albums,Func<Album,FrameworkElement> build)
    {
        this.albums=albums; this.build=build;
        Background=Brushes.Transparent; BorderThickness=new Thickness(0); Padding=new Thickness(0);
        HorizontalContentAlignment=HorizontalAlignment.Stretch;
        ScrollViewer.SetCanContentScroll(this,true); ScrollViewer.SetHorizontalScrollBarVisibility(this,ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(this,ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(this,true); VirtualizingPanel.SetVirtualizationMode(this,VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(this,ScrollUnit.Pixel); VirtualizingPanel.SetCacheLength(this,new VirtualizationCacheLength(1));
        ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        ItemTemplate=new DataTemplate { VisualTree=new FrameworkElementFactory(typeof(AlbumRowView)) };
        var style=new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        var template=new ControlTemplate(typeof(ListBoxItem)) { VisualTree=new FrameworkElementFactory(typeof(ContentPresenter)) };
        style.Setters.Add(new Setter(TemplateProperty,template)); ItemContainerStyle=style;
        SizeChanged+=(_,_)=>Reflow(); Reflow();
    }
    private void Reflow()
    {
        int count=Math.Max(1,(int)Math.Floor((ActualWidth-16)/200));
        if(count==columns) return;
        int anchor=(int)(Offset/276)*Math.Max(1,columns); columns=count;
        ItemsSource=albums.Chunk(columns).Select(row=>new AlbumRow(row,build)).ToArray();
        if(pendingOffset is null) RestoreOffset((anchor/columns)*276);
    }
    public void RestoreOffset(double offset)
    {
        pendingOffset=offset;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=> {
            if(pendingOffset is { } value && FindScroll(this) is { } scroll) { scroll.ScrollToVerticalOffset(value); pendingOffset=null; }
        }));
    }
    public void ScrollToEnd()=>FindScroll(this)?.ScrollToEnd();
    public int RealizedRows=>Items.Cast<object>().Count(item=>ItemContainerGenerator.ContainerFromItem(item) is not null);
    public bool LastRowRealized=>Items.Count==0||ItemContainerGenerator.ContainerFromIndex(Items.Count-1) is not null;
    private static ScrollViewer? FindScroll(DependencyObject node)
    {
        if(node is ScrollViewer scroll) return scroll;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) if(FindScroll(VisualTreeHelper.GetChild(node,i)) is { } found) return found;
        return null;
    }
}

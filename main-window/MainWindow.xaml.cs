using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;

namespace JellyfinMusic;
public partial class MainWindow : Window
{
    private readonly LocalStore? store;
    private TaskbarBridge? taskbar;
    private readonly TaskbarRuntime taskbarRuntime=new();
    private bool taskbarVisible,exitRequested;
    private TextBlock? taskbarStatus;
    private SavedConnection? savedConnection;
    private List<Album> albums=[];
    private readonly PlaybackSession playback=new();
    private JellyfinClient? client;
    private AudioPlayback? audio;
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim imageSlots=new(4);
    private readonly Dictionary<string,BitmapSource> imageCache=[];
    private CancellationTokenSource pageCancellation=new();
    private CancellationTokenSource nowArtCancellation=new();
    private string? nowArtKey;
    private string view="albums";
    private bool refreshing,ready,demo,connecting,closed,seekDragging;
    private readonly NavigationHistory history=new();
    private readonly Dictionary<string,List<Album>> libraryCache=[];
    private string libraryId="";
    private bool suppressControls,loadingLibrary;
    private string lastQuery="";
    private int renderVersion;
    private long queueRevision=-1;
    private int queueCount=-1;
    private Album? selectedAlbum;
    private static Brush Ink(string hex)=>(Brush)new BrushConverter().ConvertFromString(hex)!;
    public MainWindow(bool persistent=false)
    {
        InitializeComponent(); ready=true;
        foreach(var (button,icon,label) in new[] {
            (AlbumsNav,"albums","アルバム"),(FavoritesNav,"heart","お気に入り"),(QueueNav,"queue","再生キュー"),(SettingsNav,"settings","設定"),
            (Back,"back","戻る"),(ShuffleButton,"shuffle","シャッフル"),(PrevButton,"previous","前の曲"),(NextButton,"next","次の曲"),
            (AlbumLoopButton,"albumLoop","アルバムループ"),(TrackLoopButton,"trackLoop","1曲ループ"),(QueueButton,"queue","キューを開く")
        }) PlayerIcons.Set(button,icon,label);
        Seek.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,new System.Windows.Controls.Primitives.DragStartedEventHandler((_,_)=>seekDragging=true));
        Seek.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,new System.Windows.Controls.Primitives.DragCompletedEventHandler((_,_)=>{seekDragging=false;playback.Seek(Seek.Value);}));
        if(persistent) { store=new LocalStore(); try { savedConnection=store.Load(); } catch { Notice.Text="保存した接続情報を読み込めません。再ログインしてください。"; } }
        if(persistent) taskbar=new TaskbarBridge(async (command,ct)=> { await Dispatcher.InvokeAsync(()=> { if(closed||!taskbarVisible) return; switch(command) { case 1: Previous_Click(this,new RoutedEventArgs()); break; case 2: playback.Toggle(); break; case 3: playback.Next(); break; case 4: Show(); WindowState=WindowState.Normal; Activate(); break; } },DispatcherPriority.Normal,ct); });
        Closing+=(_,e)=> { if(!exitRequested&&taskbarVisible&&taskbar?.DisplayReady==true) { e.Cancel=true; Hide(); } };
        playback.Changed+=RefreshPlayback;
        timer.Tick+=(_,_)=> { if(demo) playback.Tick(1); if(taskbarStatus is not null) taskbarStatus.Text=taskbarRuntime.Failure??(taskbar?.Failure is not null?"タスクバー通信を開始できません（別の本体が起動している可能性があります）":taskbar?.DisplayReady==true?"タスクバー内UI：配置済み":taskbar?.Connected==true?"タスクバー側と通信中（UI未配置）":"タスクバー側：未接続"); if(taskbar is not null&&!IsVisible&&!taskbar.DisplayReady&&!closed) { Show(); WindowState=WindowState.Normal; } }; timer.Start();
        Closed+=(_,_)=> { closed=true; taskbar?.Dispose(); pageCancellation.Cancel(); nowArtCancellation.Cancel(); nowArtCancellation.Dispose(); timer.Stop(); audio?.Dispose(); client?.Dispose(); };
        PreviewKeyDown+=(_,e)=> {
            if(BackInput.IsBack(e.Key,e.SystemKey,Keyboard.Modifiers)) { GoBack(); e.Handled=true; return; }
            if(e.Key==System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control) { if(!Search.IsEnabled) Navigate("albums"); Search.Focus(); Search.SelectAll(); e.Handled=true; }
        };
        PreviewMouseUp+=(_,e)=> { if(e.ChangedButton==MouseButton.XButton1) { GoBack(); e.Handled=true; } };
        CommandBindings.Add(new CommandBinding(NavigationCommands.BrowseBack,(_,e)=> { GoBack(); e.Handled=true; },(_,e)=> { e.CanExecute=history.CanGoBack&&!loadingLibrary; e.Handled=true; }));
        SourceInitialized+=(_,_)=> {
            if(PresentationSource.FromVisual(this) is HwndSource source) source.AddHook((nint hwnd,int msg,nint wParam,nint lParam,ref bool handled)=> {
                if(msg==0x0319 && BackInput.IsBackAppCommand(lParam)) { GoBack(); handled=true; return (nint)1; }
                return nint.Zero;
            });
        };
        SetLibraryOptions([]);
        RenderPage(); RefreshPlayback();
        if(savedConnection is not null) Loaded+=async (_,_)=> { if(client is null && !connecting) { connecting=true; await RunAsync(()=>ConnectAsync(savedConnection.Address,savedConnection.Username,"",savedConnection)); connecting=false; if(view=="settings") RenderPage(CurrentOffset); } };
        if(persistent) {
            taskbarVisible=PlayerPreferences.LoadTaskbar(taskbarRuntime.Available);
            Loaded+=async (_,_)=> { PublishTaskbar(); if(taskbarVisible) await taskbarRuntime.StartAsync(); };
        }
    }
    private TextBlock Label(string text,double size=14,string color="#EDF0EC")=>new() { Text=text,FontSize=size,Foreground=Ink(color),TextTrimming=TextTrimming.CharacterEllipsis };
    private Button Action(string text,Action action,bool accent=false)
    {
        var b=new Button { Content=text,Margin=new Thickness(0,0,8,0) };
        if(accent) { b.Background=Ink("#C7E5A4"); b.Foreground=Ink("#17251B"); }
        b.Click+=(_,_)=>action(); return b;
    }
    private Button IconAction(string icon,string label,Action action,bool accent=false)
    {
        var button=Action(label,action,accent); button.Width=44; button.Height=40; button.Padding=new Thickness(0); button.HorizontalAlignment=HorizontalAlignment.Left; PlayerIcons.Set(button,icon,label); return button;
    }
    private void RefreshNowArtwork(Track? track)
    {
        var album=track is null?null:libraryCache.Values.SelectMany(items=>items).Concat(albums).FirstOrDefault(a=>a.Id==track.AlbumId);
        var key=track is null?null:$"{client?.Profile.Address}|{track.AlbumId}|{album?.ImageTag}";
        if(key==nowArtKey) return;
        nowArtKey=key; nowArtCancellation.Cancel(); nowArtCancellation.Dispose(); nowArtCancellation=new();
        // Separate from page cancellation: navigating must not interrupt the player cover.
        var grid=new Grid { Clip=new RectangleGeometry(new Rect(0,0,58,58),8,8) };
        var fallback=Label("♪",27); fallback.HorizontalAlignment=HorizontalAlignment.Center; fallback.VerticalAlignment=VerticalAlignment.Center;
        grid.Children.Add(fallback); NowArt.Child=grid; NowArt.ToolTip=track?.Album;
        if(track is null || client is null || string.IsNullOrEmpty(track.AlbumId)) return;
        album??=new Album(track.AlbumId,track.Album,track.Artist,0,"",track.Color,"",[]);
        var image=new Image { Stretch=Stretch.UniformToFill }; grid.Children.Add(image);
        _=LoadArtworkAsync(image,album,client,nowArtCancellation.Token);
    }
    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch(OperationCanceledException) { if(!closed) Notice.Text="通信がタイムアウトしました。再試行してください。"; }
        catch(ArgumentException e) { if(!closed) Notice.Text=e.Message; }
        catch(HttpRequestException) { if(!closed) Notice.Text="通信に失敗しました。接続先・認証情報を確認してください。"; }
        catch { if(!closed) Notice.Text="処理に失敗しました。設定を確認して再試行してください。"; }
    }
    private FrameworkElement Artwork(Album album,double size)
    {
        var grid=new Grid { Width=size,Height=size,Background=Ink(album.Color) };
        var symbol=Label("♪",size*.35,"#DAE6DB"); symbol.VerticalAlignment=VerticalAlignment.Center; symbol.HorizontalAlignment=HorizontalAlignment.Center; grid.Children.Add(symbol);
        if(client is not null && !string.IsNullOrEmpty(album.ImageTag)) { var image=new Image { Stretch=Stretch.UniformToFill }; grid.Children.Add(image); _=LoadArtworkAsync(image,album,client,pageCancellation.Token); }
        return new Border { Child=grid,CornerRadius=new CornerRadius(8),ClipToBounds=true };
    }
    private async Task LoadArtworkAsync(Image target,Album album,JellyfinClient source,CancellationToken ct)
    {
        try {
            if(imageCache.TryGetValue(album.Id,out var cached)) { target.Source=cached; return; }
            await imageSlots.WaitAsync(ct);
            try {
                var key=store?.ArtworkKey(source.Profile.Address.AbsoluteUri,source.Export("").UserId,album);
                var bytes=key is null?null:store!.ReadArtwork(key);
                if(bytes is null) { bytes=await source.GetArtworkAsync(album,ct); ct.ThrowIfCancellationRequested(); if(key is not null) store!.WriteArtwork(key,bytes); }
                ct.ThrowIfCancellationRequested();
                using var stream=new MemoryStream(bytes); var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.StreamSource=stream; image.EndInit(); image.Freeze();
                if(imageCache.Count>=96) imageCache.Clear(); imageCache[album.Id]=image; target.Source=image;
            } finally { imageSlots.Release(); }
        } catch { /* Missing artwork must not prevent browsing. */ }
    }
    private double CurrentOffset=>AlbumHost.Visibility==Visibility.Visible && AlbumHost.Content is AlbumBrowser browser?browser.Offset:PageScroll.VerticalOffset;
    private NavigationState Capture()=>new(view,selectedAlbum?.Id,Search.Text,libraryId,CurrentOffset);
    private void SetQuery(string text) { suppressControls=true; Search.Text=text; lastQuery=text; suppressControls=false; }
    private void Navigate(string target,bool remember=true)
    {
        if(loadingLibrary) return;
        if(remember && (target!=view || selectedAlbum is not null || Search.Text.Length>0)) history.Push(Capture());
        view=target; selectedAlbum=null; SetQuery(""); RenderPage();
    }
    private void Back_Click(object sender,RoutedEventArgs e)=>GoBack();
    private void GoBack()
    {
        if(loadingLibrary || history.Pop() is not { } state) return;
        libraryId=state.LibraryId;
        if(libraryCache.TryGetValue(libraryId,out var cached)) albums=cached;
        suppressControls=true; LibrarySelector.SelectedValue=libraryId; suppressControls=false;
        view=state.View; SetQuery(state.Query); selectedAlbum=albums.FirstOrDefault(a=>a.Id==state.AlbumId);
        ConnectionDetail.Text=$"{albums.Count} アルバム"; RenderPage(state.ScrollOffset);
    }
    private void SetLibraryOptions(IReadOnlyList<MusicLibrary> choices)
    {
        suppressControls=true;
        LibrarySelector.ItemsSource=new[]{new MusicLibrary("","すべての音楽ライブラリ")}.Concat(choices).ToArray();
        LibrarySelector.SelectedValue=libraryId; LibrarySelector.IsEnabled=(client is not null||demo)&&!loadingLibrary;
        suppressControls=false;
    }
    private async void Library_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!ready || suppressControls || LibrarySelector.SelectedItem is not MusicLibrary selected || selected.Id==libraryId) return;
        await RunAsync(()=>SwitchLibraryAsync(selected.Id));
        suppressControls=true; LibrarySelector.SelectedValue=libraryId; suppressControls=false;
    }
    private async Task SwitchLibraryAsync(string target)
    {
        if(loadingLibrary) return;
        var previous=Capture(); var active=client; loadingLibrary=true;
        LibrarySelector.IsEnabled=Back.IsEnabled=Search.IsEnabled=PageScroll.IsEnabled=AlbumHost.IsEnabled=false;
        Notice.Text="ライブラリを読み込み中…";
        try {
            var items=libraryCache.TryGetValue(target,out var cached)?cached:active is not null?(await active.GetAlbumsAsync(target)).ToList():albums;
            if(active!=client || closed) return;
            history.Push(previous); libraryId=target; albums=items; libraryCache[target]=items;
            selectedAlbum=null; SetQuery(""); if(view!="favorites") view="albums";
            ConnectionDetail.Text=$"{albums.Count} アルバム"; Notice.Text=demo?"デモモード（音声なし）":"";
        } finally {
            loadingLibrary=false;
            if(!closed) { LibrarySelector.IsEnabled=client is not null||demo; PageScroll.IsEnabled=AlbumHost.IsEnabled=true; RenderPage(libraryId==previous.LibraryId?previous.ScrollOffset:0); }
        }
    }
    private void Albums_Click(object sender,RoutedEventArgs e)=>Navigate("albums");
    private void Favorites_Click(object sender,RoutedEventArgs e)=>Navigate("favorites");
    private void Queue_Click(object sender,RoutedEventArgs e)=>Navigate("queue");
    private void Settings_Click(object sender,RoutedEventArgs e)=>Navigate("settings");
    private void Search_Changed(object sender,TextChangedEventArgs e) { if(ready&&!suppressControls) { if(selectedAlbum is not null) history.Push(Capture() with { Query=lastQuery }); lastQuery=Search.Text; selectedAlbum=null; RenderPage(); } }
    private void RenderPage(double offset=0)
    {
        if(!ready || closed) return;
        int version=++renderVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=> { if(version==renderVersion && PageScroll.Visibility==Visibility.Visible) PageScroll.ScrollToVerticalOffset(offset); }));
        pageCancellation.Cancel(); pageCancellation.Dispose(); pageCancellation=new();
        Page.Children.Clear(); AlbumHost.Content=null; AlbumHost.Visibility=Visibility.Collapsed; PageScroll.Visibility=Visibility.Visible;
        Search.IsEnabled=view is not "queue" and not "settings" && !loadingLibrary;
        Back.IsEnabled=history.CanGoBack&&!loadingLibrary;
        PageScroll.ScrollToVerticalOffset(offset);
        foreach(var (button,name) in new[]{(AlbumsNav,"albums"),(FavoritesNav,"favorites"),(QueueNav,"queue")}) button.Background=view==name?Ink("#303D33"):Brushes.Transparent;
        SettingsNav.Background=view=="settings"?Ink("#303D33"):Brushes.Transparent;
        if(view=="settings") { RenderSettings(); return; }
        if(view=="queue") { queueRevision=playback.Revision; queueCount=playback.Queue.Count; Heading.Text="再生キュー"; Subtitle.Text=$"{playback.Queue.Count} 曲"; RenderTracks(playback.Queue,true); return; }
        if(selectedAlbum is not null) { RenderAlbum(selectedAlbum); return; }
        var query=Search.Text.Trim();
        var filtered=albums.Where(a=>(view!="favorites" || a.IsFavorite) && (a.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||a.Artist.Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray();
        Heading.Text=view=="favorites"?"お気に入りのアルバム":"アルバム"; Subtitle.Text=$"{filtered.Length} アルバム";
        if(filtered.Length==0) {
            Page.Children.Add(Label(query.Length>0?"一致するアルバムがありません。":view=="favorites"?"お気に入りのアルバムはありません。":client is null&&!demo?"Jellyfinに接続してください。":"アルバムがありません。",14,"#A9B6AF"));
            if(client is null&&!demo) Page.Children.Add(Action("接続設定",()=>Navigate("settings"),true));
        }
        if(filtered.Length>0) RenderAlbums(filtered,offset);
    }
    private void RenderAlbums(IReadOnlyList<Album> items,double offset)
    {
        var browser=new AlbumBrowser(items,BuildAlbumCard);
        PageScroll.Visibility=Visibility.Collapsed; AlbumHost.Visibility=Visibility.Visible; AlbumHost.Content=browser;
        browser.RestoreOffset(offset);
    }
    private FrameworkElement BuildAlbumCard(Album album)
    {
            var content=new StackPanel { Width=178 }; content.Children.Add(Artwork(album,178));
            var title=Label((album.IsFavorite?"♥  ":"")+album.Title); title.FontWeight=FontWeights.SemiBold; title.Margin=new Thickness(0,12,0,5); content.Children.Add(title);
            content.Children.Add(Label(album.Artist,12,"#9DABA6"));
            var meta=Label(string.Join(" · ",new[]{album.Year>0?album.Year.ToString():"",album.Genre}.Where(s=>s.Length>0)),10,"#899B93"); meta.Margin=new Thickness(0,6,0,0); content.Children.Add(meta);
            var card=new Button { Content=content,Background=Brushes.Transparent,Padding=new Thickness(0),Margin=new Thickness(0,0,22,27),ToolTip=$"{album.Title} — {album.Artist}" };
            System.Windows.Automation.AutomationProperties.SetName(card,$"アルバム {album.Title}、{album.Artist}");
            card.Click+=async (_,_)=>await RunAsync(()=>OpenAlbumAsync(album)); return card;
    }
    private async Task OpenAlbumAsync(Album album)
    {
        if(loadingLibrary) return;
        history.Push(Capture()); selectedAlbum=album; RenderPage(); var active=client; var scope=libraryId;
        if(active is not null && album.Tracks.Count==0) {
            Notice.Text="曲を読み込み中…"; var tracks=await active.GetTracksAsync(album);
            if(active!=client || closed || scope!=libraryId) return;
            album=(albums.FirstOrDefault(a=>a.Id==album.Id)??album) with { Tracks=tracks }; ReplaceAlbum(album); playback.RegisterAlbum(tracks);
            if(selectedAlbum?.Id==album.Id) { selectedAlbum=album; RenderPage(); }
            Notice.Text="";
        }
    }
    private void ReplaceAlbum(Album album)
    {
        foreach(var list in libraryCache.Values.Append(albums).Distinct()) {
            int index=list.FindIndex(a=>a.Id==album.Id);
            if(index>=0) list[index]=album with { Tracks=album.Tracks.Count>0?album.Tracks:list[index].Tracks };
        }
    }
    private async Task ToggleFavoriteAsync(Album album)
    {
        var active=client; bool favorite=active is null?!album.IsFavorite:await active.SetFavoriteAsync(album.Id,!album.IsFavorite);
        if(active!=client || closed) return;
        album=(albums.FirstOrDefault(a=>a.Id==album.Id)??album) with { IsFavorite=favorite }; ReplaceAlbum(album); if(selectedAlbum?.Id==album.Id) selectedAlbum=album;
        Notice.Text=favorite?"お気に入りに追加しました。":"お気に入りから解除しました。"; RenderPage();
    }
    private void RenderAlbum(Album album)
    {
        Heading.Text=album.Title; Subtitle.Text=$"{album.Artist}  ·  {album.Tracks.Count} 曲";
        var hero=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,25) }; hero.Children.Add(Artwork(album,150));
        var info=new StackPanel { Margin=new Thickness(24,12,0,0) }; info.Children.Add(Label(album.Genre,12,"#AEBEAF"));
        var favorite=IconAction("heart",album.IsFavorite?"お気に入りから解除":"お気に入りに追加",()=>{}); favorite.Foreground=Ink(album.IsFavorite?"#C7E5A4":"#E9EDE8"); favorite.Margin=new Thickness(0,10,0,16);
        favorite.Click+=async (_,_)=> { favorite.IsEnabled=false; await RunAsync(()=>ToggleFavoriteAsync(album)); if(!closed) favorite.IsEnabled=true; }; info.Children.Add(favorite);
        var actions=new StackPanel { Orientation=Orientation.Horizontal };
        var play=IconAction("play",demo?"デモ再生":"アルバムを再生",()=> { playback.RegisterAlbum(album.Tracks); playback.Play(album.Tracks); },true); play.IsEnabled=album.Tracks.Count>0; actions.Children.Add(play);
        var add=IconAction("add","キューに追加",()=> { playback.RegisterAlbum(album.Tracks); playback.Enqueue(album.Tracks); Notice.Text="キューに追加しました。"; }); add.IsEnabled=album.Tracks.Count>0; actions.Children.Add(add);
        info.Children.Add(actions); hero.Children.Add(info); Page.Children.Add(hero); RenderTracks(album.Tracks);
    }
    private void RenderTracks(IReadOnlyList<Track> tracks,bool queue=false)
    {
        if(tracks.Count==0) { Page.Children.Add(Label(queue?"キューは空です。":"曲がありません。",14,"#9FAFA8")); return; }
        for(var i=0;i<tracks.Count;i++) {
            var track=tracks[i]; int index=i;
            var grid=new Grid { Margin=new Thickness(0,0,0,6),Background=Ink(i%2==0?"#1C2428":"#161D20") };
            foreach(var width in new[]{new GridLength(45),new GridLength(1,GridUnitType.Star),new GridLength(60),new GridLength(queue?95:54)}) grid.ColumnDefinitions.Add(new ColumnDefinition { Width=width });
            var number=Label((i+1).ToString("00"),12,"#8E9D96"); number.VerticalAlignment=VerticalAlignment.Center; number.HorizontalAlignment=HorizontalAlignment.Center; grid.Children.Add(number);
            var text=new StackPanel(); text.Children.Add(Label(track.Title)); var meta=Label(queue?$"{track.Artist} · {track.Album}":track.Artist,11,"#8F9F99"); meta.Margin=new Thickness(0,4,0,0); text.Children.Add(meta);
            var play=new Button { Content=text,Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,12,10,12),ToolTip=track.Title };
            play.Click+=(_,_)=> { if(queue) playback.Select(index); else { playback.RegisterAlbum(tracks); playback.Play(tracks,index); } }; Grid.SetColumn(play,1); grid.Children.Add(play);
            var duration=Label(track.Duration,12,"#9FAFA8"); duration.VerticalAlignment=VerticalAlignment.Center; Grid.SetColumn(duration,2); grid.Children.Add(duration);
            var extra=IconAction(queue?"remove":"add",queue?"キューから削除":"キューに追加",()=> { if(queue) { playback.Remove(index); RenderPage(); } else { playback.RegisterAlbum(tracks); playback.Enqueue([track]); Notice.Text="キューに追加しました。"; } }); extra.Padding=new Thickness(9); extra.VerticalAlignment=VerticalAlignment.Center; extra.ToolTip=queue?"キューから削除":"キューに追加"; Grid.SetColumn(extra,3); grid.Children.Add(extra); Page.Children.Add(grid);
        }
    }
    private void RenderSettings()
    {
        Heading.Text="設定"; Subtitle.Text=System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??"";
        if(client is not null) {
            Page.Children.Add(Label("Jellyfin 接続済み",17,"#C7E5A4"));
            Page.Children.Add(Action("ライブラリを更新",()=>_=RunAsync(async ()=> { var active=client; var scope=libraryId; var data=await active.GetAlbumsAsync(scope); if(client!=active || closed || scope!=libraryId) return; albums=data.ToList(); libraryCache[scope]=albums; Navigate("albums"); })));
            Page.Children.Add(Action("ログアウト・保存情報を削除",()=>_=RunAsync(DisconnectAsync)));
        } else {
            var form=new StackPanel { MaxWidth=480,HorizontalAlignment=HorizontalAlignment.Left };
            var address=new TextBox { MinWidth=400,Margin=new Thickness(0,6,0,16) }; var username=new TextBox { Margin=new Thickness(0,6,0,16) };
            address.Text=savedConnection?.Address??""; username.Text=savedConnection?.Username??"";
            var password=new PasswordBox { Padding=new Thickness(12),Margin=new Thickness(0,6,0,16),Background=Ink("#20282C"),Foreground=Brushes.White };
            System.Windows.Automation.AutomationProperties.SetName(address,"サーバーURL"); System.Windows.Automation.AutomationProperties.SetName(username,"ユーザー名"); System.Windows.Automation.AutomationProperties.SetName(password,"パスワード");
            form.Children.Add(Label("サーバーURL")); form.Children.Add(address); form.Children.Add(Label("ユーザー名")); form.Children.Add(username); form.Children.Add(Label("パスワード")); form.Children.Add(password);
            var connect=Action("接続",()=>{},true); connect.IsEnabled=!connecting;
            connect.Click+=async (_,_)=> { if(connecting) return; connecting=true; connect.IsEnabled=false; Notice.Text="接続中…"; await RunAsync(()=>ConnectAsync(address.Text,username.Text,password.Password)); password.Clear(); connecting=false; connect.IsEnabled=true; };
            form.Children.Add(connect); var note=Label("接続情報を保存します。パスワードは保存せず、認証トークンをWindowsで暗号化します。",12,"#9FAFA8"); note.TextWrapping=TextWrapping.Wrap; note.Margin=new Thickness(0,14,0,24); form.Children.Add(note);
            form.Children.Add(Action("デモを表示（音声なし）",LoadDemo)); Page.Children.Add(form);
        }
        if(store is not null) { Page.Children.Add(Action("画像キャッシュを削除",()=> { pageCancellation.Cancel(); store.ClearArtwork(); imageCache.Clear(); Notice.Text="画像キャッシュを削除しました。"; })); if(client is null) Page.Children.Add(Action("保存した接続情報を削除",()=> { store.Forget(); savedConnection=null; RenderPage(); })); }
                var toggle=new CheckBox { Content="タスクバー内UIを表示（試験版）", IsChecked=taskbarVisible, Foreground=Brushes.White, Margin=new Thickness(0,24,0,12) };
        toggle.Checked+=async (_,_)=> { taskbarVisible=true; SaveTaskbarPreference(); PublishTaskbar(); await taskbarRuntime.StartAsync(); };
        toggle.Unchecked+=(_,_)=> { taskbarVisible=false; SaveTaskbarPreference(); PublishTaskbar(); };
        Page.Children.Add(toggle);
        Page.Children.Add(Action("タスクバー通信を自己テスト",()=>_=RunAsync(async ()=> { await TaskbarBridgeTest.RunAsync(); Notice.Text="タスクバー通信の自己テスト：成功（Explorer表示は別途検証）"; })));
        taskbarStatus=Label(taskbar?.Connected==true?"タスクバー側と通信中":"タスクバー側：未接続",12,"#9FAFA8"); Page.Children.Add(taskbarStatus);
        Page.Children.Add(Label(taskbarRuntime.Available?"タスクバー連携を同梱しています。UI配置済みの場合のみ × で常駐します。":"試験版の連携DLLが必要です。UI配置済みの場合のみ × で常駐します。",12,"#9FAFA8"));
        Page.Children.Add(Action("完全終了",()=> { exitRequested=true; Close(); }));
    }
    public void ExitApplication() { exitRequested=true; Close(); }
    private void SaveTaskbarPreference() { if(store is null) return; try {PlayerPreferences.SaveTaskbar(taskbarVisible);} catch {Notice.Text="タスクバー表示の設定を保存できませんでした。";} }
    public async Task ConnectAsync(string address,string username,string password,SavedConnection? restore=null)
    {
        var next=new JellyfinClient(address);
        try {
            if(restore is null) await next.LoginAsync(username,password); else next.Restore(restore); var choices=await next.GetLibrariesAsync(); var items=await next.GetAlbumsAsync();
            if(closed) { if(restore is null) await next.LogoutAsync(); next.Dispose(); return; }
            if(store is not null) { var saved=next.Export(username); store.Save(saved); savedConnection=saved; }
            audio?.Dispose(); var previous=client; client=next; demo=false; playback.Clear();
            audio=new(client,playback); audio.SetVolume(Volume.Value/100); audio.Status+=text=>Notice.Text=text;
            albums=items.ToList(); libraryId=""; libraryCache.Clear(); libraryCache[libraryId]=albums; history.Clear(); SetLibraryOptions(choices);
            imageCache.Clear(); ConnectionStatus.Text="接続済み"; ConnectionStatus.Foreground=Ink("#C7E5A4"); ConnectionDetail.Text=$"{albums.Count} アルバム"; Notice.Text=""; Navigate("albums",false);
            if(previous is not null) { await previous.LogoutAsync(); previous.Dispose(); }
        } catch { if(restore is null) await next.LogoutAsync(); next.Dispose(); throw; }
    }
    private async Task DisconnectAsync()
    {
        store?.Forget(); savedConnection=null;
        var old=client; client=null; audio?.Dispose(); audio=null; playback.Clear(); albums=[]; demo=false; imageCache.Clear(); libraryId=""; libraryCache.Clear(); history.Clear(); SetLibraryOptions([]); ConnectionStatus.Text="未接続"; ConnectionStatus.Foreground=Ink("#9BA9A1"); ConnectionDetail.Text="設定から接続"; Notice.Text="切断しました。"; Navigate("settings",false);
        if(old is not null) { await old.LogoutAsync(); old.Dispose(); }
    }
    private void LoadDemo()
    {
        if(client is not null) return;
        demo=true; albums=new DemoLibrary().Albums.ToList(); foreach(var album in albums) playback.RegisterAlbum(album.Tracks);
        libraryId=""; libraryCache.Clear(); libraryCache[""]=albums; history.Clear(); SetLibraryOptions([new MusicLibrary("demo","デモ")]);
        ConnectionStatus.Text="デモ"; ConnectionDetail.Text="音声なし"; Notice.Text="デモモード（音声なし）"; Navigate("albums",false);
    }
    private void RefreshPlayback()
    {
        if(!ready || closed) return; PublishTaskbar(); refreshing=true; var track=playback.Current;
        NowTitle.Text=track?.Title??"曲を選んでください"; NowTitle.ToolTip=track?.Title; NowArtist.Text=track is null?"":track.Artist+(demo?" · デモ":""); NowArt.Background=Ink(track?.Color??"#354B45");
        RefreshNowArtwork(track);
        PlayerIcons.Set(PlayButton,playback.IsPlaying?"pause":"play",playback.IsPlaying?"一時停止":"再生"); PlayButton.IsEnabled=PrevButton.IsEnabled=NextButton.IsEnabled=Seek.IsEnabled=track is not null;
        Seek.Maximum=track?.Seconds??1; if(!seekDragging) Seek.Value=playback.Position; Elapsed.Text=TimeSpan.FromSeconds(playback.Position).ToString(@"m\:ss"); Duration.Text=track?.Duration??"0:00";
        AlbumLoopButton.Foreground=Ink(playback.Loop==LoopMode.Album?"#C7E5A4":"#829189"); TrackLoopButton.Foreground=Ink(playback.Loop==LoopMode.Track?"#C7E5A4":"#829189");
        AlbumLoopButton.Background=playback.Loop==LoopMode.Album?Ink("#303D33"):Brushes.Transparent; TrackLoopButton.Background=playback.Loop==LoopMode.Track?Ink("#303D33"):Brushes.Transparent;
        AlbumLoopButton.ToolTip=playback.Loop==LoopMode.Album?"アルバムループ：オン":"アルバムループ：オフ"; TrackLoopButton.ToolTip=playback.Loop==LoopMode.Track?"1曲ループ：オン":"1曲ループ：オフ"; refreshing=false;
        if(view=="queue" && (queueRevision!=playback.Revision || queueCount!=playback.Queue.Count)) RenderPage(CurrentOffset);
    }
    private void PublishTaskbar()=>taskbar?.Publish(new(taskbarVisible,playback.IsPlaying,playback.Current is not null,playback.Current?.Title??"曲を選んでください",playback.Current?.Artist??""));
    private void Play_Click(object sender,RoutedEventArgs e)=>playback.Toggle();
    private void Previous_Click(object sender,RoutedEventArgs e)=>playback.Previous();
    private void Next_Click(object sender,RoutedEventArgs e)=>playback.Next();
    private void Seek_Changed(object sender,RoutedPropertyChangedEventArgs<double> e) { if(ready&&!refreshing) playback.Seek(e.NewValue); }
    private void Volume_Changed(object sender,RoutedPropertyChangedEventArgs<double> e) { if(ready) audio?.SetVolume(e.NewValue/100); }
    private void Shuffle_Click(object sender,RoutedEventArgs e) { playback.Shuffle=!playback.Shuffle; ShuffleButton.Background=playback.Shuffle?Ink("#303D33"):Brushes.Transparent; ShuffleButton.ToolTip=playback.Shuffle?"シャッフル：オン":"シャッフル：オフ"; ShuffleButton.Foreground=Ink(playback.Shuffle?"#C7E5A4":"#E9EDE8"); }
    private void AlbumLoop_Click(object sender,RoutedEventArgs e) { playback.Loop=playback.Loop==LoopMode.Album?LoopMode.Off:LoopMode.Album; RefreshPlayback(); }
    private void TrackLoop_Click(object sender,RoutedEventArgs e) { playback.Loop=playback.Loop==LoopMode.Track?LoopMode.Off:LoopMode.Track; RefreshPlayback(); }
    public void PreparePreview(string page)
    {
        timer.Stop(); LoadDemo();
        if(page=="album") { selectedAlbum=albums[0]; playback.Loop=LoopMode.Album; playback.Play(selectedAlbum.Tracks); playback.Seek(74); playback.Toggle(); }
        else { view=page; if(page=="favorites") albums[0]=albums[0] with { IsFavorite=true }; }
        RenderPage();
    }
    public async Task PrepareLivePreviewAsync(string page)
    {
        if(page=="player" && albums.Count>0) {
            await OpenAlbumAsync(albums.FirstOrDefault(a=>!string.IsNullOrEmpty(a.ImageTag))??albums[0]);
            // Visual verification only: no audio output or server playback session.
            audio?.Dispose(); audio=null;
            playback.Play(selectedAlbum!.Tracks); playback.Toggle();
            Navigate("queue");
        }
        else if(page=="album" && albums.Count>0) await OpenAlbumAsync(albums[0]);
        else Navigate(page=="favorites"?"favorites":"albums");
    }
    public Task CloseConnectionAsync()=>DisconnectAsync();
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JellyfinMusic;

// Original vector artwork; independent of installed symbol fonts.
internal static class PlayerIcons
{
    internal static FrameworkElement Create(string name)
    {
        var data=name switch {
            "play" => "M8,4 L20,12 8,20 Z",
            "pause" => "M8,4 V20 M16,4 V20",
            "previous" => "M5,4 V20 M19,4 L7,12 19,20 Z",
            "next" => "M19,4 V20 M5,4 L17,12 5,20 Z",
            "shuffle" => "M3,6 H5 C10,6 14,18 19,18 H22 M18,14 L22,18 18,22 M3,18 H5 C7,18 8,16 9,14 M15,8 C17,6 18,6 22,6 M18,2 L22,6 18,10",
            "albumLoop" or "trackLoop" => "M3,10 V8 Q3,4 7,4 H20 M17,1 L20,4 17,7 M21,14 V16 Q21,20 17,20 H4 M7,17 L4,20 7,23",
            "queue" => "M3,5 H21 M3,11 H15 M3,17 H12 M18,13 L23,17 18,21 Z",
            "add" => "M3,5 H16 M3,11 H12 M3,17 H10 M18,11 V21 M13,16 H23",
            "remove" => "M5,6 H19 M9,3 H15 M7,6 L8,21 H16 L17,6 M10,10 V17 M14,10 V17",
            "heart" => "M12,21 C9,18 2,13 2,8 C2,2 9,1 12,6 C15,1 22,2 22,8 C22,13 15,18 12,21 Z",
            "back" => "M14,4 L6,12 14,20 M6,12 H22",
            "albums" => "M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z",
            "settings" => "M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M5,5 L7,7 M17,17 L19,19 M5,19 L7,17 M17,7 L19,5 M18,12 A6,6 0 1 1 6,12 A6,6 0 1 1 18,12 M14,12 A2,2 0 1 1 10,12 A2,2 0 1 1 14,12",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var canvas=new Canvas { Width=24,Height=24 };
        var path=new Path { Data=Geometry.Parse(data),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round };
        path.SetBinding(Shape.StrokeProperty,new Binding("Foreground") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1) });
        if(name is "play" or "previous" or "next") path.SetBinding(Shape.FillProperty,new Binding("Foreground") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1) });
        canvas.Children.Add(path);
        if(name=="trackLoop") {
            var one=new TextBlock { Text="1",FontSize=10,FontWeight=FontWeights.Bold };
            Canvas.SetLeft(one,9); Canvas.SetTop(one,5); canvas.Children.Add(one);
        } else if(name=="albumLoop") {
            var disc=new Ellipse { Width=7,Height=7,StrokeThickness=1.5 };
            disc.SetBinding(Shape.StrokeProperty,new Binding("Foreground") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1) });
            Canvas.SetLeft(disc,8.5); Canvas.SetTop(disc,8.5); canvas.Children.Add(disc);
        }
        return new Viewbox { Width=22,Height=22,Child=canvas };
    }

    internal static void Set(Button button,string icon,string label)
    {
        if(button.Tag as string!=icon) { button.Content=Create(icon); button.Tag=icon; }
        button.ToolTip=label;
        System.Windows.Automation.AutomationProperties.SetName(button,label);
    }
}

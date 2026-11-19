using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PS2ClassicsPackageStudio;

internal static class XmbPreview
{
    private static ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze(); return image; }
        catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { return null; }
    }
    public static void Show(Window owner, string title, string? cover, string? background, bool ps1, string audio)
    {
        var window = new Window { Title = "PS3 menu preview", Owner = owner, Width = 820, Height = 500, MinWidth = 540, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(12, 23, 40)), Foreground = Brushes.White };
        WindowTheme.Attach(window);
        var root = new Grid(); window.Content = root;
        var bg = Load(background);
        if (bg != null) root.Children.Add(new Image { Source = bg, Stretch = Stretch.UniformToFill, Opacity = 0.5 });
        else root.Background = new LinearGradientBrush(Color.FromRgb(14, 33, 63), Color.FromRgb(38, 77, 113), 30);
        var layout = new Grid { Margin = new Thickness(32) }; root.Children.Add(layout);
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new TextBlock { Text = "GAME  /  PS3 MENU PREVIEW", Foreground = Brushes.White, FontSize = 13, Opacity = 0.8 });
        var center = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(center, 1); layout.Children.Add(center);
        var icon = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(160, 8, 18, 32)), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 220, 240, 255)), BorderThickness = new Thickness(1), ClipToBounds = true };
        var art = Load(cover);
        if (art != null) icon.Child = new Image { Source = art, Stretch = Stretch.Uniform };
        else icon.Child = new TextBlock { Text = ps1 ? "PS1" : "PS2", FontSize = 34, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(icon);
        var details = new StackPanel { Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(title) ? "Game title" : title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
        details.Children.Add(new TextBlock { Text = ps1 ? "PlayStation®  ·  PS3 Classics" : "PlayStation®2  ·  PS3 Classics", FontSize = 14, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.8 });
        details.Children.Add(new TextBlock { Text = "Menu sound: " + audio, FontSize = 12, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.75 }); center.Children.Add(details);
        var note = new TextBlock { Text = "Illustrative preview. Actual PS3 layout and automatic artwork or sound may differ.", FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(note, 2); layout.Children.Add(note); window.ShowDialog();
    }
}

using System.Windows;
using System.Windows.Input;
using DBDReshade.Helpers;
using DBDReshade.Services;

namespace DBDReshade.Views;

/// <summary>Borderless full-screen viewer for a preset preview (full resolution).</summary>
public partial class FullscreenImageWindow : Window
{
    private readonly string _imagePath;

    public FullscreenImageWindow(string imagePath, string title)
    {
        InitializeComponent();
        _imagePath = imagePath;
        TitleText.Text = title;
        Owner = Application.Current.MainWindow;

        Loaded += async (_, _) =>
        {
            try
            {
                PreviewImage.Source = await Task.Run(() => ImageLoader.Load(_imagePath));
            }
            catch
            {
                LoadingText.Text = Loc.Get("Full.Failed");
                return;
            }
            LoadingText.Visibility = Visibility.Collapsed;
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter) Close();
        base.OnKeyDown(e);
    }

    // Close on button *down*: the window is opened by a click, whose button-up would land here.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Close();
        base.OnMouseLeftButtonDown(e);
    }
}

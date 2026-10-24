using System.Windows;
using System.Windows.Media.Animation;

namespace DBDReshade.Views;

/// <summary>Start-up screen shown while the app loads its data and checks for updates.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.VersionText;
    }

    public string Status
    {
        set => StatusText.Text = value;
    }

    /// <summary>Fades out, then closes.</summary>
    public Task CloseWithFadeAsync()
    {
        var done = new TaskCompletionSource();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            Close();
            done.TrySetResult();
        };
        BeginAnimation(OpacityProperty, fade);
        return done.Task;
    }
}

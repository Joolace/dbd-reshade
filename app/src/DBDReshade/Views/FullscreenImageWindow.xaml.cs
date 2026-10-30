using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DBDReshade.ViewModels;

namespace DBDReshade.Views;

/// <summary>
/// Full-screen preview with a floating bar: ← → to browse the presets, install, star, log.
/// The bar and the title fade out when the mouse stays still, so the picture is unobstructed.
/// </summary>
public partial class FullscreenImageWindow : Window
{
    private static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(2.5);

    private readonly PresetViewerViewModel _vm;
    private readonly DispatcherTimer _idle;
    private bool _chromeVisible = true;

    public FullscreenImageWindow(PresetViewerViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Owner = Application.Current.MainWindow;

        viewModel.RequestClose += (_, _) => Close();
        Loaded += async (_, _) => await viewModel.LoadAsync();

        // Close on button *down*, only on the picture: the window is opened by a click, whose button-up would land here.
        Stage.MouseLeftButtonDown += (_, _) => viewModel.CloseCommand.Execute(null);

        // Keep the log scrolled to the newest line.
        viewModel.Log.CollectionChanged += OnLogChanged;
        Closed += (_, _) =>
        {
            viewModel.Log.CollectionChanged -= OnLogChanged;
            viewModel.Detach();
            _idle?.Stop();
        };

        _idle = new DispatcherTimer { Interval = IdleTime };
        _idle.Tick += (_, _) => HideChromeIfIdle();
        MouseMove += (_, _) => ShowChrome();
        _idle.Start();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?[0] is { } item)
            LogList.ScrollIntoView(item);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ShowChrome();
        switch (e.Key)
        {
            case Key.Left or Key.A:
                _vm.PreviousCommand.Execute(null);
                break;
            case Key.Right or Key.D:
                _vm.NextCommand.Execute(null);
                break;
            case Key.I:
                _vm.InstallCommand.Execute(null);
                break;
            case Key.F:
                _vm.ToggleFavoriteCommand.Execute(null);
                break;
            case Key.L:
                _vm.ToggleLogCommand.Execute(null);
                break;
            case Key.Escape or Key.Space:
                _vm.CloseCommand.Execute(null);
                break;
            default:
                base.OnPreviewKeyDown(e);
                return;
        }
        e.Handled = true; // the arrows must not move the keyboard focus around the bar
    }

    private void ShowChrome()
    {
        _idle.Stop();
        _idle.Start();
        Cursor = null;
        if (_chromeVisible) return;
        _chromeVisible = true;
        Fade(1);
    }

    /// <summary>Hides bar and title after a while without moving the mouse (not while installing, reading the log or on the bar).</summary>
    private void HideChromeIfIdle()
    {
        _idle.Stop();
        if (!_chromeVisible || _vm.ShowLog || _vm.Installer.IsBusy || BottomArea.IsMouseOver || TopInfo.IsMouseOver)
        {
            _idle.Start();
            return;
        }
        _chromeVisible = false;
        Cursor = Cursors.None;
        Fade(0);
    }

    private void Fade(double to)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 150 : 400));
        BottomArea.BeginAnimation(OpacityProperty, animation);
        TopInfo.BeginAnimation(OpacityProperty, animation);
    }
}

using System.Collections.Specialized;
using System.Windows;
using DBDReshade.Helpers;
using DBDReshade.Services;
using DBDReshade.ViewModels;

namespace DBDReshade.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheme.SetTitleBar(this, ThemeManager.IsDark);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Keep the log scrolled to the newest line.
        vm.Log.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems?[0] is { } item)
                LogList.ScrollIntoView(item);
        };

        await vm.InitializeAsync();
    }
}

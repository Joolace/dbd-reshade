using System.Windows;
using DBDReshade.Helpers;
using DBDReshade.Services;
using DBDReshade.ViewModels;

namespace DBDReshade.Views;

public partial class MapPickerWindow : Window
{
    public MapPickerWindow(MapPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Owner = Application.Current.MainWindow;

        SourceInitialized += (_, _) => WindowTheme.SetTitleBar(this, ThemeManager.IsDark);
        viewModel.RequestClose += (_, _) => Close();
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}

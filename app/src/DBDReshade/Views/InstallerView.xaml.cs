using System.Windows.Controls;
using System.Windows.Input;
using DBDReshade.ViewModels;

namespace DBDReshade.Views;

public partial class InstallerView : UserControl
{
    public InstallerView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // Space opens the preview full screen, as in the original tool (except while typing in a text box).
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not InstallerViewModel vm) return;

        // Esc closes the filter panel; while it's open, Space selects in its lists instead of opening the preview.
        if (vm.IsFilterPanelOpen)
        {
            if (e.Key == Key.Escape)
            {
                vm.IsFilterPanelOpen = false;
                e.Handled = true;
            }
            return;
        }

        if (e.Key != Key.Space || Keyboard.FocusedElement is TextBox) return;

        vm.ShowFullscreenCommand.Execute(null);
        e.Handled = true;
    }
}

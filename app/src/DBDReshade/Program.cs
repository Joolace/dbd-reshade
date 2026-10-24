using DBDReshade.Services;
using DBDReshade.Viewer;

namespace DBDReshade;

/// <summary>
/// One exe, two modes:
/// <list type="bullet">
/// <item>normal start → the WPF app;</item>
/// <item><c>--viewer &lt;folder&gt;</c> → the Direct3D 11 map preview (formerly D3Daylight).</item>
/// </list>
/// The preview runs from a copy of this exe in its own folder, where ReShade is installed, so
/// ReShade only ever hooks the preview window and never the app's own interface.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == ViewerApp.Argument)
            return ViewerApp.Run(args);

        UpdateService.CleanUpAfterUpdate();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

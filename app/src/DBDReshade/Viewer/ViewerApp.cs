using WinForms = System.Windows.Forms;

namespace DBDReshade.Viewer;

/// <summary>
/// Entry point of the map preview mode (formerly the D3Daylight app):
/// <c>DBDReshade.exe --viewer "&lt;folder&gt;" [--title "&lt;map&gt;"] [--hint "&lt;title bar hint&gt;"]
/// [--keys "&lt;key&gt;TAB&lt;action&gt; lines"] [--theme dark|light]</c>
/// All texts come from the app, already translated.
/// </summary>
public static class ViewerApp
{
    public const string Argument = "--viewer";

    public static int Run(string[] args)
    {
        var folder = args.Length > 1 ? args[1] : null;
        var title = ReadOption(args, "--title") ?? (folder is null ? "Preview" : Path.GetFileName(folder));
        var hint = ReadOption(args, "--hint") ?? "F1: keys";
        var keys = ParseKeys(ReadOption(args, "--keys"));
        var dark = !string.Equals(ReadOption(args, "--theme"), "light", StringComparison.OrdinalIgnoreCase);

        WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
        WinForms.Application.EnableVisualStyles();
        WinForms.Application.SetCompatibleTextRenderingDefault(false);

        var images = folder is not null && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        if (images.Count == 0)
        {
            WinForms.MessageBox.Show($"No screenshots found in:\n{folder}", "DBD Reshade",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            return 1;
        }

        WinForms.Application.Run(new PreviewWindow(images, title, hint, keys, dark));
        return 0;
    }

    /// <summary>"key TAB action" lines → pairs; English defaults if the app passed nothing.</summary>
    private static IReadOnlyList<(string Key, string Action)> ParseKeys(string? text)
    {
        const string defaults =
            "← →\tPrevious / next image\nF11\tFull screen\nHome\tOpen ReShade\n" +
            "Print Screen\tTake a screenshot\nF1\tShow / hide these keys\nESC\tClose";
        var lines = (text ?? defaults)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines
            .Select(l => l.Split('\t', 2))
            .Select(p => (p[0].Trim(), p.Length > 1 ? p[1].Trim() : ""))
            .ToList();
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

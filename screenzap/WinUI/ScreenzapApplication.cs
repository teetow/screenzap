using Microsoft.UI.Xaml;
using screenzap.Components;
using screenzap.lib;

namespace screenzap.WinUI;

public sealed partial class ScreenzapApplication : Microsoft.UI.Xaml.Application
{
    private readonly ScreenzapBackground? background;
    private EditorWindow? editorWindow;

    internal ScreenzapApplication()
    {
        InitializeComponent();
        UnhandledException += (_, args) => Logger.Log($"WinUI exception: {args.Exception}");

        if (Environment.GetCommandLineArgs().Contains("--winui-smoke")) return;
        background = new ScreenzapBackground();
        background.EditorHostCreated = (host, editor) =>
        {
            host.ExternalActivateRequested = () =>
            {
                editorWindow ??= new EditorWindow(host, editor);
                editorWindow.ShowEditor();
            };
        };
        background.Closed += () => { editorWindow?.Shutdown(); Exit(); };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {

        if (background == null)
        {
            var editor = new ImageDocumentEditor();
            var host = new ClipboardDocumentHost(true, editor);
            editorWindow = new EditorWindow(host, editor);
            host.ExternalActivateRequested = editorWindow.ShowEditor;
            using var image = new System.Drawing.Bitmap(960, 600);
            using (var graphics = System.Drawing.Graphics.FromImage(image))
            {
                graphics.Clear(System.Drawing.Color.FromArgb(231, 235, 240));
                using var font = new System.Drawing.Font("Segoe UI", 32);
                graphics.DrawString("Screenzap · WinUI", font, System.Drawing.Brushes.Black, 60, 50);
                graphics.FillRectangle(System.Drawing.Brushes.SteelBlue, 60, 160, 400, 260);
                graphics.FillEllipse(System.Drawing.Brushes.DarkOrange, 510, 160, 350, 260);
            }
            var item = host.HistoryStore.AddObservedImage(image);
            if (Environment.GetCommandLineArgs().Contains("--qa-history"))
            {
                for (int index = 0; index < 31; index++)
                {
                    using var sample = new System.Drawing.Bitmap(48, 32);
                    using (var g = System.Drawing.Graphics.FromImage(sample)) g.Clear(System.Drawing.Color.FromArgb(40 + index * 5, 90, 140));
                    host.HistoryStore.AddObservedImage(sample);
                }
            }
            host.ActivateHistoryItem(item);
            editorWindow.ShowEditor();
            return;
        }
        background.StartBackgroundServices();
        // The app remains a tray utility. Opening the editor is explicit, including startup
        // smoke tests, so login never steals focus from the user's current application.
        if (Environment.GetCommandLineArgs().Contains("--open-editor")) background.OpenEditor();
    }
}

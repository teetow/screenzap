using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using screenzap.Components;
using screenzap.Components.Shared;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Color = Windows.UI.Color;
using ComboBox = Microsoft.UI.Xaml.Controls.ComboBox;
using CheckBox = Microsoft.UI.Xaml.Controls.CheckBox;
using ProgressBar = Microsoft.UI.Xaml.Controls.ProgressBar;
using FontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using Orientation = Microsoft.UI.Xaml.Controls.Orientation;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;

namespace screenzap.WinUI;

internal sealed partial class EditorWindow : Microsoft.UI.Xaml.Window
{
    private readonly ClipboardEditorHostForm host;
    private readonly ImageEditor editor;
    private readonly Grid root = new();
    private readonly EditorCanvas canvas = new() { IsTabStop = true };
    private readonly StackPanel inspector = new() { Spacing = 16, Padding = new Thickness(20) };
    private readonly StackPanel filmstrip = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(12, 6, 12, 10) };
    private readonly TextBlock documentTitle = new() { Text = "Clipboard image", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = .7 };
    private readonly TextBlock zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly Dictionary<EditorCommandId, Button> commandButtons = new();
    private readonly List<TextBlock> operationLabels = new();
    private readonly List<(EditorCommandId Command, MenuFlyoutItem Item)> menuCommands = new();
    private readonly Dictionary<ActiveTool, Button> toolButtons = new();
    private CanvasBitmap? texture;
    private System.Drawing.Bitmap? frame;
    private byte[] pixels = Array.Empty<byte>();
    private readonly RowDefinition historyRow = new() { Height = new GridLength(144) };
    private readonly DispatcherTimer stateTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private string inspectorKey = "";
    private Button? toolApplyButton;
    private readonly Dictionary<string, NumberBox> inspectorNumbers = new();
    private bool synchronizingInspector;
    private string? notification;
    private DateTime notificationUntil;
    private bool canvasInvalidationQueued;
    private bool shuttingDown;
    private uint? capturedPointer;
    private System.Windows.Forms.MouseButtons capturedButton;
    private Point lastPointerPoint;
    private long lastPress;
    private Point lastPressPoint;
    private bool historyDirty = true;
    private bool? displayedDirty;
    private ActiveTool? displayedTool;
    private bool stateRefreshQueued;
    private static readonly SolidColorBrush NormalForeground = new(Colors.LightGray);
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush SelectedToolBackground = Brush(70, 58, 38);
    private static readonly SolidColorBrush HistoryBorder = Brush(52, 54, 58);
    private double scale = 1;
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));
    private static readonly SolidColorBrush Accent = Brush(235, 174, 78);

    internal EditorWindow(ClipboardEditorHostForm host, ImageEditor editor)
    {
        this.host = host;
        this.editor = editor;
        Title = "Screenzap";
        root.RequestedTheme = ElementTheme.Dark;
        root.Background = Brush(31, 33, 37);
        root.RowDefinitions.Add(new() { Height = new GridLength(40) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(5) });
        root.RowDefinitions.Add(historyRow);
        root.RowDefinitions.Add(new() { Height = new GridLength(34) });
        BuildTitleBar();
        BuildOperations();
        BuildWorkspace();
        BuildHistory();
        BuildStatus();
        Content = root;
        root.SizeChanged += (_, _) => { foreach (var label in operationLabels) label.Visibility = root.ActualWidth < 1120 ? Visibility.Collapsed : Visibility.Visible; };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(canvas, "Image canvas");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(canvas, "EditorCanvas");
        AppWindow.Resize(new SizeInt32(1280, 900));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "res", "screenzap-icon.ico"));
        AppWindow.Closing += (_, e) => { if (!shuttingDown) { e.Cancel = true; AppWindow.Hide(); stateTimer.Stop(); } };
        editor.CanvasFocusRequested = () => canvas.Focus(FocusState.Programmatic);
        editor.SurfaceInvalidated += InvalidateCanvas;
        editor.SurfaceNotification = (title, message) => { notification = $"{title}: {message}"; notificationUntil = DateTime.UtcNow.AddSeconds(15); RefreshState(); };
        InitializeNativeDrop();
        host.HistoryStore.Changed += HistoryChanged;
        host.HistoryStore.ActiveItemChanged += ActiveHistoryChanged;
        host.HistoryStore.ItemUpdated += HistoryItemUpdated;
        host.HistoryStore.ItemPreviewRefreshed += HistoryItemUpdated;
        canvas.Renderer.Draw += DrawCanvas;
        canvas.Renderer.CreateResources += (_, _) => { texture?.Dispose(); texture = null; };
        canvas.SizeChanged += (_, _) => ResizeCanvas();
        canvas.Loaded += (_, _) => { if (root.XamlRoot != null) root.XamlRoot.Changed += (_, _) => { if (root.XamlRoot.RasterizationScale != scale) ResizeCanvas(); }; ResizeCanvas(); editor.SurfaceSetZoom(1); Execute(EditorCommandId.FitImageToView); RefreshState(); };
        canvas.LostFocus += (_, _) => editor.SurfaceSuspendText();
        canvas.PointerPressed += PointerPressed;
        canvas.PointerMoved += (_, e) => { var p = e.GetCurrentPoint(canvas); lastPointerPoint = CanvasPoint(p.Position); editor.SurfacePointer(1, lastPointerPoint, MouseButton(p.Properties)); e.Handled = true; };
        canvas.PointerExited += (_, _) => { if (!capturedPointer.HasValue) editor.SurfacePointerExit(); };
        canvas.PointerReleased += (_, e) =>
        {
            if (capturedPointer != e.Pointer.PointerId) return;
            var p = e.GetCurrentPoint(canvas); lastPointerPoint = CanvasPoint(p.Position);
            // A release can arrive ahead of a coalesced final move. Apply its final position
            // while the drag is still active before ending the gesture.
            editor.SurfacePointer(1, lastPointerPoint, capturedButton);
            editor.SurfacePointer(2, lastPointerPoint, capturedButton);
            capturedPointer = null; canvas.ReleasePointerCapture(e.Pointer); e.Handled = true;
        };
        canvas.PointerCaptureLost += (_, _) => { if (capturedPointer.HasValue) { editor.SurfacePointer(2, lastPointerPoint, capturedButton); capturedPointer = null; } };
        canvas.PointerWheelChanged += (_, e) => { var p = e.GetCurrentPoint(canvas); editor.SurfaceWheel(CanvasPoint(p.Position), p.Properties.MouseWheelDelta); e.Handled = true; };
        canvas.KeyDown += (_, e) =>
        {
            var keys = KeyData(e.Key);
            if (keys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Enter)) { Execute(EditorCommandId.CommitEdits); e.Handled = true; }
            else if (keys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.S)) { Execute(EditorCommandId.SaveAs); e.Handled = true; }
            else if (editor.SurfaceEditingText && keys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z)) { Execute(EditorCommandId.Undo); e.Handled = true; }
            else { e.Handled = editor.SurfaceKey(keys); }
            RefreshState();
        };
        canvas.KeyUp += (_, e) => editor.SurfaceKey(KeyData(e.Key), true);
        canvas.CharacterReceived += (_, e) => { editor.SurfaceCharacter((char)e.Character); e.Handled = true; };
        stateTimer.Tick += (_, _) => RefreshState();
        root.ActualThemeChanged += (_, _) => canvas.Invalidate();
    }

    internal void ShowEditor()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
        AppWindow.Show();
        Activate();
        stateTimer.Start();
        RefreshState();
        canvas.Focus(FocusState.Programmatic);
    }
    internal void Shutdown() { shuttingDown = true; stateTimer.Stop(); editor.SurfaceInvalidated -= InvalidateCanvas; host.HistoryStore.Changed -= HistoryChanged; host.HistoryStore.ActiveItemChanged -= ActiveHistoryChanged; host.HistoryStore.ItemUpdated -= HistoryItemUpdated; host.HistoryStore.ItemPreviewRefreshed -= HistoryItemUpdated; canvas.DisposeCursors(); canvas.RemoveFromVisualTree(); texture?.Dispose(); frame?.Dispose(); Close(); }
    private void HistoryChanged(object? sender, EventArgs args) { historyDirty = true; QueueStateRefresh(); }
    private void ActiveHistoryChanged(object? sender, EventArgs args) { QueueStateRefresh(); InvalidateCanvas(); }
    private void HistoryItemUpdated(object? sender, ClipboardHistoryItem item)
    {
        if (historyTiles.TryGetValue(item.Id, out var tile)) tile.NeedsThumbnail = true;
        historyDirty = true;
        QueueStateRefresh();
    }
    private void QueueStateRefresh()
    {
        if (shuttingDown || stateRefreshQueued) return;
        stateRefreshQueued = true;
        DispatcherQueue.TryEnqueue(() => { stateRefreshQueued = false; RefreshState(); });
    }
    private void InvalidateCanvas()
    {
        if (shuttingDown || canvasInvalidationQueued) return;
        canvasInvalidationQueued = true;
        DispatcherQueue.TryEnqueue(() => { canvasInvalidationQueued = false; if (shuttingDown) return; canvas.Invalidate(); RefreshState(); });
    }
    private static void Place(Grid parent, FrameworkElement child, int row, int column = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, column); parent.Children.Add(child); }
    private static IconElement Icon(string glyph) => EditorIcons.Create(glyph);
    private static Style ChromeButtonStyle => (Style)Microsoft.UI.Xaml.Application.Current.Resources["EditorChromeButtonStyle"];
    private static Button PlainButton(string glyph, string tooltip, Action action, string? label = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(Icon(glyph));
        if (label != null) content.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Style = ChromeButtonStyle, Content = content, MinWidth = 34, Height = 34, Padding = new Thickness(8, 4, 8, 4), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }
    private Button CommandButton(EditorCommandId command, string glyph, string? label = null)
    {
        var descriptor = EditorCommandCatalog.All[command];
        var button = PlainButton(glyph, EditorCommandCatalog.FormatTooltip(descriptor), () => Execute(command), label);
        commandButtons[command] = button;
        return button;
    }
    private void Execute(EditorCommandId command)
    {
        ExecuteNativeCommand(command);
        inspectorKey = "";
        RefreshState();
        canvas.Invalidate();
    }
    private void BuildTitleBar()
    {
        var bar = new Grid { Background = Brush(27, 29, 32) };
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(140) });
        var menu = BuildMenus();
        Place(bar, menu, 0);
        var drag = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        drag.Children.Add(documentTitle);
        documentTitle.HorizontalAlignment = HorizontalAlignment.Center;
        Place(bar, drag, 0, 1);
        Place(root, bar, 0);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(drag);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Colors.LightGray;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Gray;
    }
    private void BuildOperations()
    {
        // Image operations stay separate from the file/document cluster. Each remains directly
        // available; the same commands also appear in keyboard-accessible native menus.
        var bar = new Grid { Padding = new Thickness(8, 5, 8, 5), BorderBrush = Brush(48, 50, 55), BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var entry in new[] {
            (EditorCommandId.CropTool, "\uE7A8", "Crop"), (EditorCommandId.ResizeImage, "\uE740", "Resize"),
            (EditorCommandId.ExpandCanvas, "\uE9A6", "Canvas"), (EditorCommandId.RotateRight, "\uE7AD", (string?)null),
            (EditorCommandId.FlipHorizontal, "\uE8AB", null), (EditorCommandId.FlipVertical, "\uE8CB", null),
            (EditorCommandId.ReplaceBackground, "\uE790", "Background"), (EditorCommandId.ColorCorrect, "\uE793", "Color"),
            (EditorCommandId.DeJpeg, "\uE734", "De-JPEG"), (EditorCommandId.OptimizeText, "\uE8D2", "Text") })
        {
            var button = CommandButton(entry.Item1, entry.Item2, entry.Item3);
            if (entry.Item3 != null) operationLabels.Add((TextBlock)((StackPanel)button.Content).Children[1]);
            ops.Children.Add(button);
        }
        Place(bar, ops, 0);
        var files = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var entry in new[] { (EditorCommandId.Undo, "\uE7A7"), (EditorCommandId.Redo, "\uE7A6"), (EditorCommandId.Save, "\uE74E"), (EditorCommandId.Copy, "\uE8C8") }) files.Children.Add(CommandButton(entry.Item1, entry.Item2));
        var commit = CommandButton(EditorCommandId.CommitEdits, "\uE73E", "Commit");
        commit.Foreground = Accent;
        files.Children.Add(commit);
        Place(bar, files, 0, 1);
        Place(root, bar, 1);
    }
    private void BuildWorkspace()
    {
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(52) });
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(260) });
        var rail = new StackPanel { Spacing = 5, Padding = new Thickness(7, 12, 7, 12), Background = Brush(29, 31, 34) };
        foreach (var entry in new[] {
            (ActiveTool.None,EditorCommandId.SelectMoveTool,"\uE8B0"), (ActiveTool.Arrow,EditorCommandId.ArrowTool,"\uE74A"),
            (ActiveTool.Rectangle,EditorCommandId.RectangleTool,"\uE739"), (ActiveTool.Highlighter,EditorCommandId.HighlighterTool,"\uE7E6"),
            (ActiveTool.Text,EditorCommandId.TextTool,"\uE8D2"), (ActiveTool.Censor,EditorCommandId.CensorTool,"censor"),
            (ActiveTool.FreeRotate,EditorCommandId.FreeRotateTool,"\uE7AD"), (ActiveTool.Straighten,EditorCommandId.StraightenTool,"perspective") })
        {
            var button = CommandButton(entry.Item2, entry.Item3);
            button.Width = 38; button.Height = 38;
            toolButtons[entry.Item1] = button;
            rail.Children.Add(button);
        }
        rail.Children.Insert(5, CommandButton(EditorCommandId.EmojiTool, "\uE899"));
        Place(workspace, new ScrollViewer { Content = rail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 0);
        Place(workspace, canvas, 0, 1);
        var scroll = new ScrollViewer { Content = inspector, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brush(34, 36, 40) };
        Place(workspace, scroll, 0, 2);
        Place(root, workspace, 2);
    }
    private void BuildHistory()
    {
        var splitter = new Border { Background = Brush(45, 47, 51), ManipulationMode = ManipulationModes.TranslateY };
        splitter.ManipulationDelta += (_, e) => { historyRow.Height = new GridLength(Math.Clamp(historyRow.Height.Value - e.Delta.Translation.Y, 100, 260)); historyDirty = true; RefreshState(); };
        Place(root, splitter, 3);
        var history = new Grid { Background = Brush(28, 30, 33) };
        history.RowDefinitions.Add(new() { Height = new GridLength(34) });
        history.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Padding = new Thickness(16, 0, 10, 0) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Place(header, new TextBlock { Text = "History", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        actions.Children.Add(PlainButton("\uE72C", "Refresh clipboard history", async () => { if (host.RefreshSystemHistoryAsync != null) await host.RefreshSystemHistoryAsync(); }));
        actions.Children.Add(CommandButton(EditorCommandId.Duplicate, "\uE8C8"));
        actions.Children.Add(CommandButton(EditorCommandId.Revert, "\uE777"));
        actions.Children.Add(CommandButton(EditorCommandId.Delete, "\uE74D"));
        Place(header, actions, 0, 1);
        Place(history, header, 0);
        var historyScroll = new ScrollViewer { Content = filmstrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(historyScroll, "HistoryScroll");
        Place(history, historyScroll, 1);
        Place(root, history, 4);
    }
    private void BuildStatus()
    {
        var bar = new Grid { Padding = new Thickness(16, 0, 10, 0), Background = Brush(25, 27, 30) };
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Place(bar, status, 0);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        controls.Children.Add(PlainButton("\uE8A4", "Show or hide history", () => historyRow.Height = new GridLength(historyRow.Height.Value <= 34 ? 144 : 34)));
        controls.Children.Add(zoomLabel);
        controls.Children.Add(PlainButton("\uE71F", "Zoom out", () => editor.SurfaceSetZoom(Math.Max(.01m, editor.SurfaceZoom / 1.25m))));
        controls.Children.Add(PlainButton("\uE71E", "Zoom in", () => editor.SurfaceSetZoom(Math.Min(32m, editor.SurfaceZoom * 1.25m))));
        controls.Children.Add(CommandButton(EditorCommandId.FitImageToView, "\uE9A6", "Fit"));
        controls.Children.Add(PlainButton("\uE740", "Actual size", () => editor.SurfaceSetZoom(1), "100%"));
        controls.Children.Add(CommandButton(EditorCommandId.ToggleTransparencyGrid, "\uE80A", "Transparency"));
        Place(bar, controls, 0, 1);
        Place(root, bar, 5);
    }
    private void RefreshState()
    {
        if (shuttingDown) return;
        var item = host.HistoryStore.ActiveItem;
        bool dirty = item?.IsDirty == true;
        if (displayedDirty != dirty)
        {
            displayedDirty = dirty;
            documentTitle.Text = "Clipboard image" + (dirty ? "  •" : "");
            documentTitle.Foreground = dirty ? Accent : NormalForeground;
        }
        foreach (var pair in commandButtons) pair.Value.IsEnabled = host.CanExecuteHostCommand(pair.Key);
        foreach (var pair in menuCommands) pair.Item.IsEnabled = host.CanExecuteHostCommand(pair.Command);
        if (displayedTool != editor.CurrentTool)
        {
            displayedTool = editor.CurrentTool;
            foreach (var pair in toolButtons)
            {
                bool selected = pair.Key == editor.CurrentTool;
                pair.Value.Background = selected ? SelectedToolBackground : TransparentBrush;
                pair.Value.Foreground = selected ? Accent : NormalForeground;
            }
        }
        RefreshHistorySelection();
        canvas.SetCursor(Enum.Parse<Microsoft.UI.Input.InputSystemCursorShape>(editor.SurfaceCursor));
        var size = editor.SurfaceImageSize;
        if (toolApplyButton != null) toolApplyButton.IsEnabled = editor.SurfaceCanApply;
        status.Text = notification != null && DateTime.UtcNow < notificationUntil ? notification : size.IsEmpty ? "Copy an image to begin" : $"{size.Width} × {size.Height} px   ·   {host.CurrentStatusText}";
        zoomLabel.Text = $"{editor.SurfaceZoom * 100:0.#}%";
        var key = editor.SurfaceInspectorKind;
        if (key != inspectorKey) { inspectorKey = key; BuildInspector(key); }
        RefreshInspectorValues(key);
        if (historyDirty && !nativeDragInProgress) { historyDirty = false; SynchronizeHistory(); }
    }
    private void RefreshInspectorValues(string kind)
    {
        // Canvas drags and aspect-locked resizing change these values outside the inspector.
        // Keep fields current without replacing controls, interrupting typing, or recording undo.
        void Update(string label, double value)
        {
            if (inspectorNumbers.TryGetValue(label, out var box) && box.FocusState == FocusState.Unfocused && box.Value != value)
                box.Value = value;
        }
        synchronizingInspector = true;
        try
        {
            if (kind == "FreeRotate") Update("Angle (°)", editor.SurfaceAngleValue);
            if (kind == "Layer" && editor.SurfaceSelectedLayer is { } layer)
            {
                Update("X", layer.Frame.X);
                Update("Y", layer.Frame.Y);
                Update("Width", layer.Frame.Width);
                Update("Height", layer.Frame.Height);
                Update("Rotation (°)", layer.RotationDeg);
            }
        }
        finally { synchronizingInspector = false; }
    }
    private void ResizeCanvas()
    {
        double nextScale = root.XamlRoot?.RasterizationScale ?? 1;
        var size = new Size(Math.Max(1, (int)Math.Round(canvas.ActualWidth * nextScale)), Math.Max(1, (int)Math.Round(canvas.ActualHeight * nextScale)));
        if (nextScale == scale && size == editor.SurfaceViewportSize) return;
        if (nextScale != scale) { texture?.Dispose(); texture = null; }
        scale = nextScale;
        editor.ResizeSurface(size);
        canvas.Invalidate();
    }
    private void DrawCanvas(CanvasControl sender, CanvasDrawEventArgs args)
    {
        int width = Math.Max(1, (int)Math.Round(canvas.ActualWidth * scale)), height = Math.Max(1, (int)Math.Round(canvas.ActualHeight * scale));
        if (frame == null || frame.Width != width || frame.Height != height)
        {
            frame?.Dispose(); texture?.Dispose(); texture = null;
            frame = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
            pixels = new byte[width * height * 4];
        }
        using (var graphics = System.Drawing.Graphics.FromImage(frame)) editor.RenderSurface(graphics);
        var data = frame.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); } finally { frame.UnlockBits(data); }
        texture ??= CanvasBitmap.CreateFromBytes(sender, pixels, width, height, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 96f * (float)scale);
        texture.SetPixelBytes(pixels);
        args.DrawingSession.DrawImage(texture, new Windows.Foundation.Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
    }
    private Point CanvasPoint(Windows.Foundation.Point p) => new((int)Math.Round(p.X * scale), (int)Math.Round(p.Y * scale));
    private static System.Windows.Forms.MouseButtons MouseButton(Microsoft.UI.Input.PointerPointProperties p, bool released = false) => p.IsMiddleButtonPressed || p.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased ? System.Windows.Forms.MouseButtons.Middle : p.IsRightButtonPressed || p.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased ? System.Windows.Forms.MouseButtons.Right : p.IsLeftButtonPressed || released ? System.Windows.Forms.MouseButtons.Left : System.Windows.Forms.MouseButtons.None;
    private void PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        canvas.Focus(FocusState.Pointer);
        canvas.CapturePointer(e.Pointer);
        capturedPointer = e.Pointer.PointerId;
        var p = e.GetCurrentPoint(canvas); var point = CanvasPoint(p.Position);
        capturedButton = MouseButton(p.Properties); lastPointerPoint = point;
        bool doubleClick = Environment.TickCount64 - lastPress < System.Windows.Forms.SystemInformation.DoubleClickTime && Math.Abs(point.X - lastPressPoint.X) < 5 && Math.Abs(point.Y - lastPressPoint.Y) < 5;
        editor.SurfacePointer(0, point, MouseButton(p.Properties), doubleClick ? 2 : 1);
        if (doubleClick) editor.SurfacePointer(3, point, MouseButton(p.Properties), 2);
        lastPress = Environment.TickCount64; lastPressPoint = point; e.Handled = true;
        inspectorKey = ""; RefreshState();
    }
    private static System.Windows.Forms.Keys KeyData(VirtualKey key)
    {
        var keys = (System.Windows.Forms.Keys)(int)key;
        if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0) keys |= System.Windows.Forms.Keys.Control;
        if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0) keys |= System.Windows.Forms.Keys.Shift;
        if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & CoreVirtualKeyStates.Down) != 0) keys |= System.Windows.Forms.Keys.Alt;
        return keys;
    }
}

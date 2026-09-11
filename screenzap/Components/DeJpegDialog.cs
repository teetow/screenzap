using System.Drawing;
using System.Windows.Forms;
using screenzap.lib;

namespace screenzap;

internal sealed class DeJpegDialog : Form
{
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(400, 0), Text = "Preparing JPEG cleanup…" };
    private readonly ProgressBar progressBar = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
    private readonly Bitmap source;
    private readonly Func<bool> isCurrent;
    private readonly Func<IDeJpegFilter>? backendFactory;
    private CancellationTokenSource? pending;
    private bool finished;
    internal Bitmap? Result { get; private set; }
    internal string StatusForDiagnostics => status.Text;
    internal bool FinishedForDiagnostics => finished;

    internal DeJpegDialog(Image image, Func<bool> isCurrent, Func<IDeJpegFilter>? backendFactory = null)
    {
        source = new Bitmap(image);
        this.isCurrent = isCurrent;
        this.backendFactory = backendFactory;
        Text = "De-JPEG";
        ClientSize = new Size(440, 130);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3, MinimumSize = new Size(440, 130) };
        layout.Controls.Add(status, 0, 0);
        layout.Controls.Add(progressBar, 0, 1);
        cancel.Anchor = AnchorStyles.Right;
        layout.Controls.Add(cancel, 0, 2);
        Controls.Add(layout);
        CancelButton = cancel;
        Shown += async (_, _) => await CleanAsync();
        FormClosing += (_, _) => pending?.Cancel();
    }

    internal async Task CleanAsync()
    {
        if (pending != null || finished) return;
        using var cancellation = new CancellationTokenSource();
        pending = cancellation;
        var progress = new Progress<string>(message => { if (!IsDisposed && !finished) status.Text = message; });
        using var snapshot = new Bitmap(source);
        try
        {
            if (!isCurrent()) throw new InvalidOperationException("The image changed. Close and try again.");
            var backend = backendFactory?.Invoke() ?? new OnnxDeJpegFilter();
            var result = await Task.Run(async () =>
            {
                using var input = new DeJpegBitmap(snapshot);
                var bytes = await backend.CleanAsync(input.Png, progress, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                return input.Restore(bytes);
            }, cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested || !isCurrent())
            {
                result.Dispose();
                if (!IsDisposed) status.Text = "The image changed. Close and try again.";
                return;
            }
            Result = result;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Cleanup cancelled."; }
        catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        finally
        {
            pending = null;
            finished = true;
            if (!IsDisposed) { cancel.Text = "Close"; progressBar.Visible = false; }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pending?.Cancel();
            Result?.Dispose(); Result = null;
            source.Dispose();
        }
        base.Dispose(disposing);
    }
}

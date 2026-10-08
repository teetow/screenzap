using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace screenzap;
public partial class ImageDocumentEditor
{
    private EmojiRecentStore emojiRecentStore = new();
    internal bool AddEmojiAtClientPoint(string emoji, Point clientPoint)
    {
        if (!HasEditableImage || !EmojiRecentStore.IsEmoji(emoji))
            return false;
        SetActiveTool(ActiveTool.None);
        FinalizeActiveTextAnnotation();
        var before = CloneTextAnnotations();
        var annotation = new TextAnnotation
        {
            Text = emoji,
            FontFamily = "Segoe UI Emoji",
            FontSize = 72f,
            FontStyle = FontStyle.Regular,
            TextColor = Color.Black,
            OutlineThickness = 0f,
            CaretPosition = emoji.Length
        };
        using (var graphics = viewport.CreateGraphics())
        {
            var bounds = annotation.GetBounds(graphics);
            var center = ViewportToImage(clientPoint);
            annotation.Position = new Point(center.X - bounds.Width / 2, center.Y - bounds.Height / 2);
        }

        SelectAnnotation(null);
        textAnnotations.Add(annotation);
        SelectTextAnnotation(annotation);
        PushTextUndoStep(before, CloneTextAnnotations());
        emojiRecentStore.Record(emoji);
        viewport.Invalidate();
        return true;
    }
}

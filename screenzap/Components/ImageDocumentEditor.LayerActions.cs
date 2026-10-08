using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace screenzap
{
    /// <summary>Floating-layer selection, visibility, merging, deletion and stacking.</summary>
    public partial class ImageDocumentEditor
    {
        /// <summary>
        /// The next free "Paste N". Derived from the live stack rather than a running counter so
        /// it stays right after layers come back from history, an undo, or a delete — a counter
        /// would restart at 1 on restore and hand a restored "Paste 2" a twin.
        /// </summary>
        private int NextPasteLayerNumber()
        {
            int highest = 0;
            foreach (var layer in imageLayers)
            {
                var name = layer.Name;
                if (name != null && name.StartsWith("Paste ", StringComparison.Ordinal) && int.TryParse(name.AsSpan(6), out int number))
                {
                    highest = Math.Max(highest, number);
                }
            }

            return highest + 1;
        }

        private void SelectLayerFromPanel(ImageLayer? layer)
        {
            // The Background row means "stop addressing the paste" — the tools go back to the
            // canvas, which is what you want when a paste is floating over something you still
            // need to fix.
            SelectImageLayer(layer == null ? -1 : imageLayers.IndexOf(layer));
            RequestCanvasFocus();
        }

        private void ToggleLayerMuteFromPanel(ImageLayer layer)
        {
            int index = imageLayers.IndexOf(layer);
            if (index < 0)
            {
                return;
            }

            var layersBefore = CloneLayers();
            layer.IsVisible = !layer.IsVisible;
            // Muting rides the same layer-only undo step as a drag or a resize, so Ctrl+Z walks
            // back through visibility changes like any other layer edit.
            PushLayerOnlyUndoStep(layersBefore);
            viewport?.Invalidate();
        }

        private void ApplyLayerFromPanel(ImageLayer layer)
        {
            int index = imageLayers.IndexOf(layer);
            if (index >= 0)
            {
                ApplyFloatingPaste(index);
            }
        }

        private void DeleteLayerFromPanel(ImageLayer layer)
        {
            int index = imageLayers.IndexOf(layer);
            if (index >= 0)
            {
                TryDeleteLayerAt(index);
            }
        }

        /// <summary>
        /// Restack <paramref name = "layer"/> into <paramref name = "slot"/>. Rows read top-most
        /// first and <see cref = "imageLayers"/> is stored bottom-first, so the move happens in
        /// visual order and is written back reversed.
        /// </summary>
        private bool MoveLayerToRowSlot(ImageLayer layer, int slot)
        {
            int count = imageLayers.Count;
            int layerIndex = imageLayers.IndexOf(layer);
            if (layerIndex < 0)
            {
                return false;
            }

            int from = count - 1 - layerIndex;
            int to = Math.Clamp(slot, 0, count);
            // Dropping below where the row currently sits loses a slot to its own removal.
            if (to > from)
            {
                to--;
            }

            if (to == from)
            {
                return false;
            }

            var layersBefore = CloneLayers();
            var visual = new List<ImageLayer>(count);
            for (int i = count - 1; i >= 0; i--)
            {
                visual.Add(imageLayers[i]);
            }

            visual.RemoveAt(from);
            visual.Insert(to, layer);
            imageLayers.Clear();
            for (int i = visual.Count - 1; i >= 0; i--)
            {
                imageLayers.Add(visual[i]);
            }

            selectedLayerIndex = imageLayers.IndexOf(layer);
            PushLayerOnlyUndoStep(layersBefore);
            viewport?.Invalidate();
            return true;
        }
    }
}

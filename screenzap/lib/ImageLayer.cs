using System;
using System.Drawing;

namespace screenzap
{
    /// <summary>
    /// Non-destructive image layer ("smart object"). The Source bitmap is owned by the layer
    /// and never mutated by transforms; Frame/Fill/RotationDeg/Mask are sidecars applied at render time.
    /// </summary>
    internal sealed class ImageLayer : IDisposable
    {
        public ImageLayer(Bitmap source, RectangleF frame)
            : this(source, frame, new RectangleF(0f, 0f, source?.Width ?? 0, source?.Height ?? 0), 0f, null)
        {
        }

        public ImageLayer(Bitmap source, RectangleF frame, RectangleF fill, float rotationDeg, Bitmap? mask)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Frame = frame;
            Fill = fill;
            RotationDeg = rotationDeg;
            Mask = mask;
        }

        /// <summary>
        /// Identity that survives cloning, restacking and a round-trip through the history
        /// store. Persistence names the layer's PNG after it, so reordering the stack does not
        /// rewrite every layer file.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        public Bitmap Source { get; }
        public RectangleF Frame { get; set; }
        public RectangleF Fill { get; set; }
        public float RotationDeg { get; set; }
        public Bitmap? Mask { get; }

        /// <summary>
        /// Muting a layer (the panel's eye) hides it everywhere the layer counts as content:
        /// the viewport, the flattened composite, and hit-testing. It is not a view-only
        /// toggle — a muted layer is left out of a save or a copy exactly as it is left off
        /// the screen.
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Display name for the layers panel. Assigned once when the layer is created and
        /// carried through clones so a row keeps its identity across undo and restacking —
        /// numbering by stack position would renumber every row each time one is glued down.
        /// </summary>
        public string Name { get; set; } = "Paste";

        public ImageLayer Clone()
        {
            return new ImageLayer(
                new Bitmap(Source),
                Frame,
                Fill,
                RotationDeg,
                Mask == null ? null : new Bitmap(Mask))
            {
                Id = Id,
                IsVisible = IsVisible,
                Name = Name,
            };
        }

        public void Dispose()
        {
            Source.Dispose();
            Mask?.Dispose();
        }
    }
}

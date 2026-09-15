using System;
using System.Collections.Generic;
using System.Linq;

namespace screenzap
{
    /// <summary>
    /// Everything an image item carries on top of its base bitmap: annotation shapes, text
    /// annotations and floating image layers. Exactly the set <c>BuildCompositeImage</c> draws
    /// over the base, and exactly the set that has to travel together — stashed into a history
    /// item, restored into the editor, and written to the manifest.
    ///
    /// It is one object because it kept not being one. The three lists used to be three
    /// properties passed around in parallel, and a floating paste survived none of a restart
    /// because the serializer was taught about two of them and not the third. Adding a fourth
    /// kind of overlay now means adding a list here and the compiler pointing at Clone.
    /// </summary>
    internal sealed class DocumentOverlay : IDisposable
    {
        public List<AnnotationShape> Shapes { get; init; } = new List<AnnotationShape>();

        public List<TextAnnotation> Texts { get; init; } = new List<TextAnnotation>();

        public List<ImageLayer> Layers { get; init; } = new List<ImageLayer>();

        public bool IsEmpty => Shapes.Count == 0 && Texts.Count == 0 && Layers.Count == 0;

        public DocumentOverlay Clone()
        {
            return new DocumentOverlay
            {
                Shapes = Shapes.Select(shape => shape.Clone()).ToList(),
                Texts = Texts.Select(text => text.Clone()).ToList(),
                Layers = Layers.Select(layer => layer.Clone()).ToList(),
            };
        }

        /// <summary>
        /// Layers are the only part holding unmanaged pixels, so they are the only part that
        /// needs releasing — but the whole overlay owns its contents, so it disposes as a unit
        /// rather than leaving callers to remember which list was special.
        /// </summary>
        public void Dispose()
        {
            foreach (var layer in Layers)
            {
                layer.Dispose();
            }

            Layers.Clear();
            Shapes.Clear();
            Texts.Clear();
        }
    }
}

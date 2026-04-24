using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace screenzap
{
    internal interface IUndoStep : IDisposable
    {
    }

    internal sealed class ImageUndoStep : IUndoStep
    {
        public ImageUndoStep(Rectangle region, Bitmap? before, Bitmap? after, Rectangle selectionBefore, Rectangle selectionAfter, bool replacesImage, List<AnnotationShape>? shapesBefore, List<AnnotationShape>? shapesAfter, List<TextAnnotation>? textsBefore = null, List<TextAnnotation>? textsAfter = null)
        {
            Region = region;
            Before = before;
            After = after;
            SelectionBefore = selectionBefore;
            SelectionAfter = selectionAfter;
            ReplacesImage = replacesImage;
            ShapesBefore = shapesBefore;
            ShapesAfter = shapesAfter;
            TextsBefore = textsBefore;
            TextsAfter = textsAfter;
        }

        public Rectangle Region { get; }
        public Bitmap? Before { get; }
        public Bitmap? After { get; }
        public Rectangle SelectionBefore { get; }
        public Rectangle SelectionAfter { get; }
        public bool ReplacesImage { get; }
        public List<AnnotationShape>? ShapesBefore { get; }
        public List<AnnotationShape>? ShapesAfter { get; }
        public List<TextAnnotation>? TextsBefore { get; }
        public List<TextAnnotation>? TextsAfter { get; }

        public void Dispose()
        {
            Before?.Dispose();
            After?.Dispose();
        }
    }

    internal sealed class UndoRedo : IDisposable
    {
        private readonly List<IUndoStep> _steps = new List<IUndoStep>();
        private int _currentIndex = -1;

        public bool CanUndo => _currentIndex >= 0;
        public bool CanRedo => _currentIndex < _steps.Count - 1;

        public void Clear()
        {
            foreach (var step in _steps)
            {
                step.Dispose();
            }

            _steps.Clear();
            _currentIndex = -1;
        }

        public void Push(IUndoStep? step)
        {
            if (step == null)
            {
                return;
            }

            if (_currentIndex < _steps.Count - 1)
            {
                for (int i = _currentIndex + 1; i < _steps.Count; i++)
                {
                    _steps[i].Dispose();
                }

                _steps.RemoveRange(_currentIndex + 1, _steps.Count - (_currentIndex + 1));
            }

            _steps.Add(step);
            _currentIndex = _steps.Count - 1;
        }

        public IUndoStep? Undo()
        {
            if (!CanUndo)
            {
                return null;
            }

            var step = _steps[_currentIndex];
            _currentIndex--;
            return step;
        }

        public IUndoStep? Redo()
        {
            if (!CanRedo)
            {
                return null;
            }

            _currentIndex++;
            return _steps[_currentIndex];
        }

        /// <summary>
        /// Snapshot of the internal step list + cursor for stashing between editor contexts.
        /// Steps are transferred by reference; ownership moves to the snapshot until restored.
        /// </summary>
        internal sealed class Snapshot
        {
            internal List<IUndoStep> Steps = new List<IUndoStep>();
            internal int Index = -1;
        }

        internal static Snapshot? CloneSnapshot(Snapshot? snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            var clone = new Snapshot
            {
                Index = snapshot.Index
            };

            foreach (var step in snapshot.Steps)
            {
                if (CloneStep(step) is IUndoStep clonedStep)
                {
                    clone.Steps.Add(clonedStep);
                }
            }

            clone.Index = Math.Clamp(clone.Index, -1, clone.Steps.Count - 1);
            return clone;
        }

        private static IUndoStep? CloneStep(IUndoStep step)
        {
            if (step is ImageUndoStep image)
            {
                return new ImageUndoStep(
                    image.Region,
                    image.Before == null ? null : new Bitmap(image.Before),
                    image.After == null ? null : new Bitmap(image.After),
                    image.SelectionBefore,
                    image.SelectionAfter,
                    image.ReplacesImage,
                    image.ShapesBefore?.Select(shape => shape.Clone()).ToList(),
                    image.ShapesAfter?.Select(shape => shape.Clone()).ToList(),
                    image.TextsBefore?.Select(text => text.Clone()).ToList(),
                    image.TextsAfter?.Select(text => text.Clone()).ToList());
            }

            if (step is TextAnnotationUndoStep text)
            {
                return new TextAnnotationUndoStep(
                    text.Before.Select(entry => entry.Clone()).ToList(),
                    text.After.Select(entry => entry.Clone()).ToList());
            }

            return null;
        }

        internal Snapshot ExtractState()
        {
            var snapshot = new Snapshot
            {
                Steps = new List<IUndoStep>(_steps),
                Index = _currentIndex
            };
            _steps.Clear();
            _currentIndex = -1;
            return snapshot;
        }

        internal void RestoreState(Snapshot? snapshot)
        {
            // Dispose any existing owned steps before replacing.
            foreach (var step in _steps)
            {
                step.Dispose();
            }
            _steps.Clear();
            _currentIndex = -1;

            if (snapshot == null)
            {
                return;
            }

            _steps.AddRange(snapshot.Steps);
            _currentIndex = snapshot.Index;
        }

        public void Dispose()
        {
            Clear();
        }
    }
}

using System.Drawing;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class UndoRedoTests
    {
        [Fact]
        public void Push_Undo_Redo_RoundTripsSingleStep()
        {
            using var stack = new screenzap.UndoRedo();
            using var before = new Bitmap(4, 4);
            using var after = new Bitmap(4, 4);

            stack.Push(new screenzap.ImageUndoStep(
                new Rectangle(0, 0, 4, 4),
                new Bitmap(before),
                new Bitmap(after),
                Rectangle.Empty,
                Rectangle.Empty,
                false,
                null,
                null));

            Assert.True(stack.CanUndo);
            Assert.False(stack.CanRedo);

            var undo = stack.Undo();
            Assert.NotNull(undo);
            Assert.False(stack.CanUndo);
            Assert.True(stack.CanRedo);

            var redo = stack.Redo();
            Assert.Same(undo, redo);
            Assert.True(stack.CanUndo);
            Assert.False(stack.CanRedo);
        }

        [Fact]
        public void Push_AfterUndo_TruncatesRedoBranch()
        {
            using var stack = new screenzap.UndoRedo();

            var step1 = CreateStep();
            var step2 = CreateStep();
            var step3 = CreateStep();

            stack.Push(step1);
            stack.Push(step2);

            Assert.NotNull(stack.Undo());
            Assert.True(stack.CanRedo);

            stack.Push(step3);

            Assert.False(stack.CanRedo);
            var undone = stack.Undo();
            Assert.Same(step3, undone);
        }

        [Fact]
        public void RevisionIdentity_SurvivesTransferCloneAndUndo_AndChangesOnBranch()
        {
            using var stack = new screenzap.UndoRedo();
            var initial = stack.CurrentRevision;
            stack.Push(CreateStep());
            var first = stack.CurrentRevision;
            stack.Push(CreateStep());
            var second = stack.CurrentRevision;
            Assert.NotEqual(initial, first);
            Assert.NotEqual(first, second);
            stack.Undo();
            Assert.Equal(first, stack.CurrentRevision);
            var snapshot = stack.ExtractState();
            using var clone = new screenzap.UndoRedo();
            clone.RestoreState(screenzap.UndoRedo.CloneSnapshot(snapshot));
            stack.RestoreState(snapshot);
            Assert.Equal(first, stack.CurrentRevision);
            Assert.Equal(first, clone.CurrentRevision);
            clone.Redo();
            Assert.Equal(second, clone.CurrentRevision);
            stack.Push(CreateStep());
            Assert.NotEqual(second, stack.CurrentRevision);
            stack.Undo();
            Assert.Equal(first, stack.CurrentRevision);
            stack.Undo();
            Assert.Equal(initial, stack.CurrentRevision);
        }

        private static screenzap.ImageUndoStep CreateStep()
        {
            var before = new Bitmap(2, 2);
            var after = new Bitmap(2, 2);

            return new screenzap.ImageUndoStep(
                new Rectangle(0, 0, 2, 2),
                before,
                after,
                Rectangle.Empty,
                Rectangle.Empty,
                false,
                null,
                null);
        }
    }
}
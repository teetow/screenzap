using System;
using System.Drawing;
using System.Windows.Forms;

namespace screenzap.Components
{
    /// <summary>
    /// Opens the Windows color dialog while carrying its custom swatches from one invocation to
    /// the next. WinForms otherwise allocates a fresh custom-color palette for every ColorDialog.
    /// </summary>
    internal static class SharedColorDialog
    {
        private static readonly object PaletteLock = new object();
        private static int[] customColors = new int[16];

        internal static DialogResult ShowDialog(
            IWin32Window owner,
            Color initialColor,
            out Color selectedColor,
            bool anyColor = false)
        {
            return ShowDialogCore(
                initialColor,
                out selectedColor,
                anyColor,
                dialog => dialog.ShowDialog(owner));
        }

        internal static DialogResult ShowDialogForDiagnostics(
            Color initialColor,
            out Color selectedColor,
            Func<ColorDialog, DialogResult> showDialog)
        {
            return ShowDialogCore(initialColor, out selectedColor, anyColor: false, showDialog);
        }

        private static DialogResult ShowDialogCore(
            Color initialColor,
            out Color selectedColor,
            bool anyColor,
            Func<ColorDialog, DialogResult> showDialog)
        {
            int[] palette;
            lock (PaletteLock)
            {
                palette = (int[])customColors.Clone();
            }

            using var dialog = new ColorDialog
            {
                Color = initialColor,
                FullOpen = true,
                AnyColor = anyColor,
                CustomColors = palette,
            };

            DialogResult result;
            try
            {
                result = showDialog(dialog);
            }
            finally
            {
                lock (PaletteLock)
                {
                    customColors = (int[])dialog.CustomColors.Clone();
                }
            }

            selectedColor = dialog.Color;
            return result;
        }
    }
}

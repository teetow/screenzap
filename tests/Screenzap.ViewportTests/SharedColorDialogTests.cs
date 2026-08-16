using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using screenzap.Components;
using Xunit;

namespace Screenzap.ViewportTests
{
    public sealed class SharedColorDialogTests
    {
        [Fact]
        public void CustomColorsCarryAcrossDialogInvocations()
        {
            StaTest.Run(() =>
            {
                int[] expectedPalette = Enumerable.Range(1, 16)
                    .Select(index => index | ((index + 1) << 8) | ((index + 2) << 16))
                    .ToArray();
                SharedColorDialog.ShowDialogForDiagnostics(
                    Color.Red,
                    out _,
                    dialog =>
                    {
                        dialog.CustomColors = expectedPalette;
                        return DialogResult.Cancel;
                    });

                SharedColorDialog.ShowDialogForDiagnostics(
                    Color.Blue,
                    out _,
                    dialog =>
                    {
                        Assert.Equal(expectedPalette, dialog.CustomColors);
                        return DialogResult.Cancel;
                    });
            });
        }
    }
}

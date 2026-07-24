using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace screenzap.Components
{
    /// <summary>
    /// Modal editor for the two transparency-checkerboard square colors. Each color has a swatch
    /// (click to open the system color picker) and a linked #RRGGBB hex field; the two stay in
    /// sync. Colors are opaque (the checkerboard has no meaningful alpha).
    /// </summary>
    internal sealed class CheckerboardColorsDialog : Form
    {
        private readonly ColorRow lightRow;
        private readonly ColorRow darkRow;

        public Color LightColor => lightRow.Value;
        public Color DarkColor => darkRow.Value;

        public CheckerboardColorsDialog(Color light, Color dark)
        {
            Text = "Transparency checkerboard colors";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(320, 150);

            lightRow = new ColorRow("Light squares", light) { Top = 18 };
            darkRow = new ColorRow("Dark squares", dark) { Top = 58 };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(80, 28),
            };
            okButton.Location = new Point(ClientSize.Width - 176, ClientSize.Height - 40);

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(80, 28),
            };
            cancelButton.Location = new Point(ClientSize.Width - 88, ClientSize.Height - 40);

            Controls.Add(lightRow);
            Controls.Add(darkRow);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        /// <summary>Label + clickable swatch + hex field, kept in sync.</summary>
        private sealed class ColorRow : Panel
        {
            private readonly Panel swatch;
            private readonly TextBox hexBox;
            private Color value;
            private bool updating;

            public Color Value => value;

            public ColorRow(string caption, Color initial)
            {
                value = Opaque(initial);
                Size = new Size(300, 32);
                Left = 12;

                var label = new Label
                {
                    Text = caption,
                    AutoSize = false,
                    Size = new Size(110, 24),
                    Location = new Point(0, 4),
                    TextAlign = ContentAlignment.MiddleLeft,
                };

                swatch = new Panel
                {
                    Size = new Size(48, 24),
                    Location = new Point(116, 2),
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = value,
                    Cursor = Cursors.Hand,
                };
                swatch.Click += (_, _) => PickViaDialog();

                hexBox = new TextBox
                {
                    Location = new Point(176, 3),
                    Size = new Size(110, 24),
                    Text = ToHex(value),
                    MaxLength = 7,
                };
                hexBox.TextChanged += HexChanged;
                hexBox.Leave += (_, _) => { hexBox.Text = ToHex(value); }; // normalize on blur

                Controls.Add(label);
                Controls.Add(swatch);
                Controls.Add(hexBox);
            }

            private void PickViaDialog()
            {
                using var dialog = new ColorDialog
                {
                    Color = value,
                    FullOpen = true,
                    AnyColor = true,
                };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetValue(Opaque(dialog.Color), updateHex: true);
                }
            }

            private void HexChanged(object? sender, EventArgs e)
            {
                if (updating)
                {
                    return;
                }

                if (TryParseHex(hexBox.Text, out var parsed))
                {
                    SetValue(parsed, updateHex: false);
                }
            }

            private void SetValue(Color newValue, bool updateHex)
            {
                value = newValue;
                swatch.BackColor = newValue;
                if (updateHex)
                {
                    updating = true;
                    hexBox.Text = ToHex(newValue);
                    updating = false;
                }
            }

            private static Color Opaque(Color c) => Color.FromArgb(255, c.R, c.G, c.B);

            private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            private static bool TryParseHex(string? text, out Color color)
            {
                color = Color.Black;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                var s = text.Trim().TrimStart('#');
                if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                {
                    return false;
                }

                color = Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                return true;
            }
        }
    }
}

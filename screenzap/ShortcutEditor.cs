using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ShortcutEditor : Form
    {
        public KeyCombo currentCombo;
        private KeyCombo originalCombo;
        KeysConverter keyConv = new KeysConverter();
        public ShortcutEditor(KeyCombo combo)
        {
            originalCombo = combo;
            currentCombo = combo;
            InitializeComponent();
            this.textBoxShortcut.Text = currentCombo.ToString();
        }

        private bool isModifier(KeyEventArgs e)
        {
            return (e.KeyCode & Keys.KeyCode) != Keys.None;
        }

        private void textBoxShortcut_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            textBoxShortcut.Text = keyConv.ConvertToString(e.KeyData);
            var pressedKey = (e.KeyData ^ e.Modifiers);
            currentCombo.Key = pressedKey;
            currentCombo.Modifiers = e.Modifiers;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            if (!originalCombo.Equals(currentCombo))
            {
                try
                {
                    KeyboardHook hook = new KeyboardHook();
                    hook.RegisterHotKey(currentCombo.getModifierKeys(), currentCombo.Key);
                    hook.Dispose();
                }
                catch
                {
                    MessageBox.Show("Can't register the hotkey. Please pick a better one.");
                    return;
                }
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShortcutEditor_Shown(object sender, EventArgs e)
        {
            this.textBoxShortcut.Focus();
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class KeyboardHook : IDisposable
{
    // Registers a hot key with Windows.
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    // Unregisters the hot key with Windows.
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Represents the window that is used internally to get the messages.
    /// </summary>
    private class Window : NativeWindow, IDisposable
    {
        private static int WM_HOTKEY = 0x0312;

        public Window()
        {
            // create the handle for the window.
            this.CreateHandle(new CreateParams());
        }

        /// <summary>
        /// Overridden to get the notifications.
        /// </summary>
        /// <param name="m"></param>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // check if we got a hot key pressed.
            if (m.Msg == WM_HOTKEY)
            {
                // get the keys.
                Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
                ModifierKeys modifier = (ModifierKeys)((int)m.LParam & 0xFFFF);

                // invoke the event to notify the parent.
                if (KeyPressed != null)
                    KeyPressed(this, new KeyPressedEventArgs(modifier, key));
            }
        }

        public event EventHandler<KeyPressedEventArgs> KeyPressed;

        #region IDisposable Members

        public void Dispose()
        {
            this.DestroyHandle();
        }

        #endregion
    }

    private Window _window = new Window();
    private int _currentId;

    public KeyboardHook()
    {
        // register the event of the inner native window.
        _window.KeyPressed += delegate (object sender, KeyPressedEventArgs args)
        {
            if (KeyPressed != null)
                KeyPressed(this, args);
        };
    }

    /// <summary>
    /// Registers a hot key in the system.
    /// </summary>
    /// <param name="modifiers">The modifiers that are associated with the hot key.</param>
    /// <param name="key">The key itself that is associated with the hot key.</param>
    public void RegisterHotKey(ModifierKeys modifiers, Keys key)
    {
        // increment the counter.
        _currentId = _currentId + 1;

        // register the hot key.
        if (!RegisterHotKey(_window.Handle, _currentId, (uint)modifiers, (uint)key))
            throw new InvalidOperationException("Couldn’t register the hot key.");
    }

    public void UnregisterHotkey(int id)
    {
        if (!UnregisterHotKey(_window.Handle, id))
        {
            throw new InvalidOperationException("Couldn't unregister the hot key.");
        }
        else
        {
            _currentId--;
        }
    }

    /// <summary>
    /// A hot key has been pressed.
    /// </summary>
    public event EventHandler<KeyPressedEventArgs> KeyPressed;

    #region IDisposable Members

    public void Dispose()
    {
        // unregister all the registered hot keys.
        for (int i = _currentId; i > 0; i--)
        {
            UnregisterHotKey(_window.Handle, i);
        }

        // dispose the inner native window.
        _window.Dispose();
    }

    #endregion
}

/// <summary>
/// Event Args for the event that is fired after the hot key has been pressed.
/// </summary>
public class KeyPressedEventArgs : EventArgs
{
    private ModifierKeys _modifier;
    private Keys _key;

    internal KeyPressedEventArgs(ModifierKeys modifier, Keys key)
    {
        _modifier = modifier;
        _key = key;
    }

    public ModifierKeys Modifier
    {
        get { return _modifier; }
    }

    public Keys Key
    {
        get { return _key; }
    }
}

/// <summary>
/// The enumeration of possible modifiers.
/// </summary>
[Flags]
public enum ModifierKeys : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

public struct KeyCombo
{
    public Keys Modifiers;
    public Keys Key;
    private static KeysConverter converter = new KeysConverter();

    public KeyCombo(object comboObj) : this()
    {
        this.Modifiers = (Keys)comboObj & Keys.Modifiers;
        this.Key = (Keys)comboObj & ~Keys.Modifiers;
    }

    public KeyCombo(Keys modifiers, Keys key)
    {
        this.Modifiers = modifiers;
        this.Key = key;
    }

    public KeyCombo(string keyCombo)
    {
        keyCombo = keyCombo.Replace("-", "+");
        var comboObj = converter.ConvertFromString(keyCombo);
        this = new KeyCombo(comboObj);
    }

    public override string ToString()
    {
        return converter.ConvertToString(this.Modifiers | this.Key);
    }

    internal ModifierKeys getModifierKeys()
    {
        ModifierKeys keys = new ModifierKeys();

        if ((this.Modifiers & Keys.Control) == Keys.Control)
            keys |= ModifierKeys.Control;
        if ((this.Modifiers & Keys.Alt) == Keys.Alt)
            keys |= ModifierKeys.Alt;
        if ((this.Modifiers & Keys.Shift) == Keys.Shift)
            keys |= ModifierKeys.Shift;
        if ((this.Modifiers & Keys.LWin) == Keys.LWin)
            keys |= ModifierKeys.Win;
        if ((this.Modifiers & Keys.RWin) == Keys.RWin)
            keys |= ModifierKeys.Win;
        return keys;
    }
}

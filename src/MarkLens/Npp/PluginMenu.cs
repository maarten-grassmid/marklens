using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MarkLens.Npp
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MenuCommand();

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShortcutKey
    {
        public byte IsCtrl;
        public byte IsAlt;
        public byte IsShift;
        public byte Key;

        public ShortcutKey(bool ctrl, bool alt, bool shift, System.Windows.Forms.Keys key)
        {
            IsCtrl = (byte)(ctrl ? 1 : 0);
            IsAlt = (byte)(alt ? 1 : 0);
            IsShift = (byte)(shift ? 1 : 0);
            Key = (byte)key;
        }
    }

    /// <summary>
    /// Builds the native FuncItem array handed to Notepad++ via getFuncsArray.
    /// Layout (x64): wchar_t name[64]; fnptr; int cmdID; bool init2Check; ShortcutKey* sk.
    /// </summary>
    internal sealed class PluginMenu
    {
        private const int NameChars = 64;
        private const int ItemSize = NameChars * 2 + 8 + 4 + 4 + 8; // 152

        private readonly List<(string Name, MenuCommand Command, bool Checked, ShortcutKey? Key)> _items =
            new List<(string, MenuCommand, bool, ShortcutKey?)>();
        private readonly List<IntPtr> _allocations = new List<IntPtr>();
        private IntPtr _native;

        public int Count => _items.Count;

        public int Add(string name, MenuCommand command, bool initiallyChecked = false, ShortcutKey? key = null)
        {
            // An exception escaping a native callback would terminate Notepad++.
            MenuCommand safe = command == null ? null : (MenuCommand)(() =>
            {
                try { command(); }
                catch (Exception ex) { Main.ReportError(ex); }
            });
            _items.Add((name, safe, initiallyChecked, key));
            return _items.Count - 1;
        }

        public int AddSeparator() => Add("-", null);

        public IntPtr NativePointer
        {
            get
            {
                if (_native == IntPtr.Zero)
                    Build();
                return _native;
            }
        }

        /// <summary>Command id assigned by Notepad++ after it reads the array.</summary>
        public int CommandId(int index) => Marshal.ReadInt32(_native, index * ItemSize + NameChars * 2 + 8);

        private void Build()
        {
            _native = Marshal.AllocHGlobal(ItemSize * _items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                var (name, command, isChecked, key) = _items[i];
                IntPtr item = _native + i * ItemSize;

                var chars = new byte[NameChars * 2];
                var src = System.Text.Encoding.Unicode.GetBytes(name.Length > NameChars - 1 ? name.Substring(0, NameChars - 1) : name);
                Array.Copy(src, chars, src.Length);
                Marshal.Copy(chars, 0, item, chars.Length);

                IntPtr fn = command == null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(command);
                Marshal.WriteIntPtr(item, NameChars * 2, fn);
                Marshal.WriteInt32(item, NameChars * 2 + 8, 0);
                Marshal.WriteInt32(item, NameChars * 2 + 12, isChecked ? 1 : 0);

                IntPtr keyPtr = IntPtr.Zero;
                if (key.HasValue)
                {
                    keyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ShortcutKey>());
                    Marshal.StructureToPtr(key.Value, keyPtr, false);
                    _allocations.Add(keyPtr);
                }
                Marshal.WriteIntPtr(item, NameChars * 2 + 16, keyPtr);
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace UnionMpvPlayer.Helpers
{
    public static class WindowManagement
    {
        // Window styles
        public const int WS_CHILD = 0x40000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;

        // Window positioning flags
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        private const int WS_EX_NOPARENTNOTIFY = 0x4;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;

        // Z-Order constants
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOP = new IntPtr(0);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // Window relationship constants
        public const int GWL_HWNDPARENT = -8;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);


        public static void UpdateWindowBounds(IntPtr windowHandle, int x, int y, int width, int height)
        {
            SetWindowPos(
                windowHandle,
                IntPtr.Zero,   // Don't change Z-order
                x, y,
                width, height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW
            );
        }

        public static void EnsureWindowZOrder(IntPtr windowHandle, bool bottom = true)
        {
            SetWindowPos(
                windowHandle,
                bottom ? HWND_BOTTOM : HWND_TOP,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE
            );
        }

        public static void DestroyWindowSafe(IntPtr windowHandle)
        {
            if (windowHandle != IntPtr.Zero)
            {
                DestroyWindow(windowHandle);
            }
        }
    }
}
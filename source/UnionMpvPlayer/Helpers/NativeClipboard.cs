using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace UnionMpvPlayer.Helpers
{
    public static class NativeClipboard
    {
        [DllImport("user32.dll")]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalUnlock(IntPtr hMem);

        public const uint CF_DIB = 8; // Clipboard format for DIB (Device Independent Bitmap)
        public const uint GMEM_MOVEABLE = 0x0002;

        public static void SetImage(Image image)
        {
            if (!OpenClipboard(IntPtr.Zero))
                throw new InvalidOperationException("Unable to open clipboard");

            try
            {
                EmptyClipboard();

                // Convert the image to a DIB
                using (var memoryStream = new MemoryStream())
                {
                    // Save as BMP
                    image.Save(memoryStream, ImageFormat.Bmp);

                    // Skip the BMP file header (first 14 bytes) to get the DIB data
                    var dibData = memoryStream.ToArray();
                    var dibDataStartIndex = 14; // BMP header is 14 bytes
                    var dibSize = dibData.Length - dibDataStartIndex;

                    // Allocate global memory for the DIB
                    var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)dibSize);
                    if (hMem == IntPtr.Zero)
                        throw new OutOfMemoryException("Failed to allocate global memory");

                    // Lock the global memory and copy the DIB data
                    var ptr = GlobalLock(hMem);
                    if (ptr == IntPtr.Zero)
                        throw new InvalidOperationException("Failed to lock global memory");

                    try
                    {
                        Marshal.Copy(dibData, dibDataStartIndex, ptr, dibSize);
                    }
                    finally
                    {
                        GlobalUnlock(hMem);
                    }

                    // Set the DIB data to the clipboard
                    if (SetClipboardData(CF_DIB, hMem) == IntPtr.Zero)
                        throw new InvalidOperationException("Failed to set clipboard data");
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

}

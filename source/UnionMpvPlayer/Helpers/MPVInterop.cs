using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace UnionMpvPlayer.Helpers
{
    public static class MPVInterop
    {
        private const string LibraryName = "libmpv-2.dll";
        private static IntPtr libraryHandle;

        static MPVInterop()
        {
            string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string libraryPath = Path.Combine(assetsPath, LibraryName);

            if (!File.Exists(libraryPath))
            {
                throw new DllNotFoundException($"MPV library not found at: {libraryPath}");
            }

            libraryHandle = LoadLibrary(libraryPath);
            if (libraryHandle == IntPtr.Zero)
            {
                throw new DllNotFoundException($"Failed to load MPV library from: {libraryPath}");
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        #region MPV Format and Event Enums

        public enum mpv_end_file_reason
        {
            MPV_END_FILE_REASON_EOF = 0,
            MPV_END_FILE_REASON_STOP = 2,
            MPV_END_FILE_REASON_QUIT = 3,
            MPV_END_FILE_REASON_ERROR = 4,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_end_file_event
        {
            public mpv_end_file_reason reason;
            public int error;
            public IntPtr playlist_entry_id;
            public IntPtr playlist_insert_id;
            public int playlist_insert_num_entries;
        }

        public enum mpv_format
        {
            MPV_FORMAT_NONE = 0,
            MPV_FORMAT_STRING = 1,
            MPV_FORMAT_OSD_STRING = 2,
            MPV_FORMAT_FLAG = 3,
            MPV_FORMAT_INT64 = 4,
            MPV_FORMAT_DOUBLE = 5,
            MPV_FORMAT_NODE = 6,
            MPV_FORMAT_NODE_ARRAY = 7,
            MPV_FORMAT_NODE_MAP = 8,
            MPV_FORMAT_BYTE_ARRAY = 9
        }

        public enum mpv_event_id
        {
            MPV_EVENT_NONE = 0,
            MPV_EVENT_SHUTDOWN = 1,
            MPV_EVENT_LOG_MESSAGE = 2,
            MPV_EVENT_GET_PROPERTY_REPLY = 3,
            MPV_EVENT_SET_PROPERTY_REPLY = 4,
            MPV_EVENT_COMMAND_REPLY = 5,
            MPV_EVENT_START_FILE = 6,
            MPV_EVENT_END_FILE = 7,
            MPV_EVENT_FILE_LOADED = 8,
            MPV_EVENT_SEEK = 20,
            MPV_EVENT_PLAYBACK_RESTART = 21,
            MPV_EVENT_PROPERTY_CHANGE = 22,
        }

        #endregion

        #region MPV Structures
        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event
        {
            public mpv_event_id event_id;
            public int error;
            public ulong reply_userdata;
            public IntPtr data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event_property
        {
            public IntPtr name;
            public mpv_format format;
            public IntPtr data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_node
        {
            public mpv_format format;
            public node_data u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct node_data
        {
            [FieldOffset(0)] public IntPtr str;  // Pointer to a string
            [FieldOffset(0)] public double dbl; // Double value
            [FieldOffset(0)] public long i64;   // 64-bit integer value
        }

        #endregion

        #region Core MPV Functions
        [DllImport(LibraryName, EntryPoint = "mpv_create", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(LibraryName, EntryPoint = "mpv_initialize", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(LibraryName, EntryPoint = "mpv_terminate_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(LibraryName, EntryPoint = "mpv_free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);
        #endregion

        #region Property Functions
        [DllImport(LibraryName, EntryPoint = "mpv_get_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name,
            mpv_format format, out node_data data);

        [DllImport(LibraryName, EntryPoint = "mpv_get_property_string", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(LibraryName, EntryPoint = "mpv_set_option_string", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string data);

        [DllImport(LibraryName, EntryPoint = "mpv_observe_property", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_observe_property(IntPtr ctx, ulong reply_userdata,
            [MarshalAs(UnmanagedType.LPStr)] string name, mpv_format format);
        #endregion

        #region Command Functions
        [DllImport(LibraryName, EntryPoint = "mpv_command", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_command_native(IntPtr ctx, IntPtr args);

        [DllImport(LibraryName, EntryPoint = "mpv_request_event", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_request_event(IntPtr ctx, mpv_event_id event_id, int enable);

        [DllImport(LibraryName, EntryPoint = "mpv_command_async", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_command_async_native(IntPtr ctx, ulong reply_userdata, IntPtr args);

        public static int mpv_command_async(IntPtr ctx, ulong reply_userdata, string[] args)
        {
            if (ctx == IntPtr.Zero)
                throw new ArgumentNullException(nameof(ctx));
            if (args == null || args.Length == 0)
                throw new ArgumentNullException(nameof(args));

            IntPtr[] nativeArgs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    nativeArgs[i] = Marshal.StringToHGlobalAnsi(args[i]);
                }
                nativeArgs[args.Length] = IntPtr.Zero;

                IntPtr argv = Marshal.AllocHGlobal(nativeArgs.Length * IntPtr.Size);
                try
                {
                    Marshal.Copy(nativeArgs, 0, argv, nativeArgs.Length);
                    return mpv_command_async_native(ctx, reply_userdata, argv);
                }
                finally
                {
                    Marshal.FreeHGlobal(argv);
                }
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (nativeArgs[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(nativeArgs[i]);
                    }
                }
            }
        }

        public static int mpv_command(IntPtr ctx, string[] args)
        {
            if (ctx == IntPtr.Zero)
                throw new ArgumentNullException(nameof(ctx));
            if (args == null || args.Length == 0)
                throw new ArgumentNullException(nameof(args));

            IntPtr[] nativeArgs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    nativeArgs[i] = Marshal.StringToHGlobalAnsi(args[i]);
                }
                nativeArgs[args.Length] = IntPtr.Zero;

                IntPtr argv = Marshal.AllocHGlobal(nativeArgs.Length * IntPtr.Size);
                try
                {
                    Marshal.Copy(nativeArgs, 0, argv, nativeArgs.Length);
                    return mpv_command_native(ctx, argv);
                }
                finally
                {
                    Marshal.FreeHGlobal(argv);
                }
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (nativeArgs[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(nativeArgs[i]);
                    }
                }
            }
        }
        #endregion

        #region Event Functions
        [DllImport(LibraryName, EntryPoint = "mpv_wait_event", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        [DllImport(LibraryName, EntryPoint = "mpv_error_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_error_string(int error);
        #endregion

        #region Helper Methods
        public static string GetError(int error)
        {
            var ptr = mpv_error_string(error);
            if (ptr != IntPtr.Zero)
            {
                return Marshal.PtrToStringAnsi(ptr) ?? "Unknown error";
            }
            return "Unknown error";
        }

        public static string? GetStringProperty(IntPtr ctx, string name)
        {
            var ptr = mpv_get_property_string(ctx, name);
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                mpv_free(ptr);
            }
        }

        public static double? GetDoubleProperty(IntPtr ctx, string name)
        {
            var ptr = mpv_get_property_string(ctx, name);
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                var str = Marshal.PtrToStringUTF8(ptr);
                if (str != null && double.TryParse(str,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double result))
                {
                    return result;
                }
            }
            finally
            {
                mpv_free(ptr);
            }

            return null;
        }

        public static int? GetIntProperty(IntPtr ctx, string name)
        {
            try
            {
                IntPtr result = mpv_get_property(ctx, name, mpv_format.MPV_FORMAT_INT64, out var data);
                if (result == 0) // MPV_SUCCESS
                {
                    return (int)data.i64;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting integer property '{name}': {ex.Message}");
            }

            return null;
        }
        #endregion
    }
}

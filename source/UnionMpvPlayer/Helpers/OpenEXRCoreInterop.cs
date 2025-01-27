using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace UnionMpvPlayer.Helpers
{
    public static class OpenEXRCoreInterop
    {
        private const string OpenEXRCoreLib = "OpenEXRCore-3_3";

        public const int EXR_ERR_SUCCESS = 0;
        public const int EXR_ERR_UNKNOWN = -1;
        public const int EXR_ERR_INVALID_ARGUMENT = -2;
        public const int EXR_ERR_FILE_ACCESS = -3;
        public const int EXR_ERR_FILE_BAD_HEADER = -4;
        public const int EXR_ERR_NOT_OPEN = -5;

        private static bool _dllLoaded;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static bool TryLoadDll(string dllPath)
        {
            try
            {
                var handle = LoadLibrary(dllPath);
                return handle != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load {dllPath}: {ex.Message}");
                return false;
            }
        }

        // Basic enums
        public enum PixelType
        {
            UINT = 0,
            HALF = 1,
            FLOAT = 2
        }

        // Required structs
        [StructLayout(LayoutKind.Sequential)]
        public struct exr_attr_chlist_t
        {
            public uint num_channels;
            public uint reserved;
            public IntPtr entries;
        }
            
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct exr_attr_chlist_entry_t
        {
            public uint length;
            public uint flags;
            public IntPtr name;
            public uint pixel_type;
            public uint x_sampling;
            public uint y_sampling;
            public uint reserved;
        }

        // Core methods
        [DllImport(OpenEXRCoreLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int exr_start_read(out IntPtr context, string filename, IntPtr ctxt);

        [DllImport(OpenEXRCoreLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void exr_finish(ref IntPtr handle);

        [DllImport(OpenEXRCoreLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int exr_get_channels(IntPtr context, int part_index, ref IntPtr channelList);

        [DllImport(OpenEXRCoreLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr exr_get_error_code_as_string(int code);

        // DLL loading helpers
        private static readonly string[] Dependencies = new[] {
            "zlib1.dll",
            "deflate.dll",
            "libpng16.dll",
            "jpeg62.dll",
            "Iex-3_3.dll",
            "IlmThread-3_3.dll",
            "Imath-3_1.dll",
            "OpenEXRCore-3_3.dll"
        };

        static OpenEXRCoreInterop()
        {   
            
            try
            {
                var searchPaths = new[]
                {
                Path.Combine(AppContext.BaseDirectory, "Assets", "OpenImageIO"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "OpenEXR"),
                Path.Combine(Environment.SystemDirectory),  // Add system32
                AppContext.BaseDirectory
            };

                // Load dependencies
                foreach (var dependency in Dependencies)
                {
                    bool depLoaded = false;
                    foreach (var path in searchPaths)
                    {
                        var dllPath = Path.GetFullPath(Path.Combine(path, dependency));
                        Debug.WriteLine($"Checking for dependency: {dllPath}");
                        if (File.Exists(dllPath) && TryLoadDll(dllPath))
                        {
                            depLoaded = true;
                            break;
                        }
                    }
                    if (!depLoaded)
                    {
                        throw new DllNotFoundException($"Required dependency {dependency} not found or failed to load");
                    }
                }

                // Load OpenEXRCore
                bool coreLoaded = false;
                foreach (var path in searchPaths)
                {
                    var dllPath = Path.GetFullPath(Path.Combine(path, $"{OpenEXRCoreLib}.dll"));
                    Debug.WriteLine($"Checking for OpenEXRCore: {dllPath}");
                    if (File.Exists(dllPath) && TryLoadDll(dllPath))
                    {
                        _dllLoaded = true;
                        coreLoaded = true;
                        break;
                    }
                }

                if (!coreLoaded)
                {
                    throw new DllNotFoundException($"Could not find or load {OpenEXRCoreLib}.dll");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize OpenEXR: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }
    }
}
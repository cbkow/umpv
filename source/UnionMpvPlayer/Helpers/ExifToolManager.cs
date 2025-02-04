using System;
using System.IO;
using System.IO.Compression;

namespace UnionMpvPlayer.Helpers
{
    public static class ExifToolManager
    {
        public static string GetExifToolPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string exifToolDir = Path.Combine(localAppData, "umpv", "exiftool");
            string exifToolExe = Path.Combine(exifToolDir, "exiftool.exe");

            if (!File.Exists(exifToolExe))
            {
                string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "exiftool.zip");

                Directory.CreateDirectory(exifToolDir);

                ZipFile.ExtractToDirectory(zipPath, exifToolDir);
            }

            return exifToolExe;
        }
    }
}

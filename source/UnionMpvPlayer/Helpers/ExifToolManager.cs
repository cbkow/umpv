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

            // Check if the exiftool.exe already exists
            if (!File.Exists(exifToolExe))
            {
                // Path to the embedded resource zip in the Assets folder
                string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "exiftool.zip");

                // Ensure the target directory exists
                Directory.CreateDirectory(exifToolDir);

                // Extract the zip file
                ZipFile.ExtractToDirectory(zipPath, exifToolDir);
            }

            return exifToolExe;
        }
    }
}

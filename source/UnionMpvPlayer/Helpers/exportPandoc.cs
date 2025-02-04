using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnionMpvPlayer.Views;

namespace UnionMpvPlayer.Helpers
{
    public partial class exportPandoc
    {
        public static string ExtractResourceIfNeeded(string resourceName, string outputFileName, string targetFolderPath)
        {
            string targetPath = Path.Combine(targetFolderPath, outputFileName);

            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            var assembly = typeof(NotesView).Assembly;
            using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
            using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
            {
                if (resourceStream == null)
                    throw new Exception($"Resource '{resourceName}' not found.");
                resourceStream.CopyTo(fileStream);
            }
            return targetPath;
        }

        public static void ExtractWkHtmlToPdfIfNeeded(string wkhtmltopdfFolderPath)
        {
            string wkhtmltopdfExePath = Path.Combine(wkhtmltopdfFolderPath, "wkhtmltopdf.exe");

            if (!Directory.Exists(wkhtmltopdfFolderPath) || !File.Exists(wkhtmltopdfExePath))
            {
                Directory.CreateDirectory(wkhtmltopdfFolderPath);

                var assembly = typeof(NotesView).Assembly;
                using var resourceStream = assembly.GetManifestResourceStream("UnionMpvPlayer.Assets.wkhtmltopdf.zip");
                if (resourceStream == null)
                {
                    throw new Exception("wkhtmltopdf resource not found.");
                }

                using var archive = new ZipArchive(resourceStream);
                foreach (var entry in archive.Entries)
                {
                    string destinationPath = Path.Combine(wkhtmltopdfFolderPath, entry.FullName);
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }
            }
        }

        public static async Task ConvertHtmlToPdfAsync(string htmlFilePath, string pdfFilePath, string wkhtmltopdfPath, string settingsDirectory)
        {
            string cssFilePath = Path.Combine(settingsDirectory, "pandoc", "convertMarkdownToHTML.css");

            string arguments = $"--enable-local-file-access \"{htmlFilePath}\" \"{pdfFilePath}\"";

            if (File.Exists(cssFilePath))
            {
                arguments = $"--user-style-sheet \"{cssFilePath}\" " + arguments;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = wkhtmltopdfPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
        }

        private static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("www."))
                return url;

            return url.Replace(" ", "%20");
        }

        private static string PreprocessMarkdown(string markdown)
        {
            var codeBlockPattern = @"```[\s\S]*?```";
            var segments = Regex.Split(markdown, $"({codeBlockPattern})");

            for (int i = 0; i < segments.Length; i++)
            {
                if (i % 2 == 0) 
                {
                    segments[i] = Regex.Replace(
                        segments[i],
                        @"\[([^\]]+)\]\(([^\)]+)\)",
                        m => $"[{m.Groups[1].Value}]({SanitizeUrl(m.Groups[2].Value)})"
                    );

                    segments[i] = Regex.Replace(
                        segments[i],
                        @"^(\s*)- \[([ x])\] (.+)$",
                        "$1- $3",
                        RegexOptions.Multiline
                    );

                    segments[i] = Regex.Replace(
                        segments[i],
                        @"==([^=]+)==",
                        "<span style=\"background-color: yellow\">$1</span>"
                    );
                }
            }
            return string.Join("", segments);
        }

        public static async Task ConvertMarkdownToHtml(string mdFilePath, string htmlFilePath, string pandocPath)
        {
            string markdown = File.ReadAllText(mdFilePath);
            string processedMarkdown = PreprocessMarkdown(markdown);
            string tempMdPath = Path.GetTempFileName();
            File.WriteAllText(tempMdPath, processedMarkdown);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pandocPath,
                    Arguments = $"-s -o \"{htmlFilePath}\" --metadata title=\"{Path.GetFileNameWithoutExtension(mdFilePath)}\" \"{tempMdPath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            File.Delete(tempMdPath);
        }

        public static void EmbedCssInline(string htmlFilePath, string cssFilePath)
        {
            string cssContent = File.ReadAllText(cssFilePath);
            string htmlContent = File.ReadAllText(htmlFilePath);

            string styleTag = $"<style>\n{cssContent}\n</style>";
            htmlContent = Regex.Replace(htmlContent, @"(<head.*?>)", $"$1\n{styleTag}\n");

            File.WriteAllText(htmlFilePath, htmlContent);
        }

        public static void EmbedImagesAsBase64(string htmlFilePath, string mdDirectory)
        {
            string htmlContent = File.ReadAllText(htmlFilePath);

            string imgTagPattern = @"<img\s+src=""([^""]+)""";
            htmlContent = Regex.Replace(htmlContent, imgTagPattern, match =>
            {
                string imagePath = match.Groups[1].Value;

                if (imagePath.StartsWith("file:///"))
                {
                    imagePath = new Uri(imagePath).LocalPath;
                }

                if (!Path.IsPathRooted(imagePath))
                {
                    imagePath = Path.Combine(mdDirectory, imagePath);
                }

                string base64Image = ConvertImageToBase64(imagePath);
                if (base64Image != null)
                {
                    return $"<img src=\"{base64Image}\"";
                }

                return match.Value;
            });

            File.WriteAllText(htmlFilePath, htmlContent);
        }

        private static string ConvertImageToBase64(string filePath)
        {
            string extension = Path.GetExtension(filePath).TrimStart('.').ToLower();
            string mimeType = extension switch
            {
                "png" => "image/png",
                "jpg" => "image/jpeg",
                "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "svg" => "image/svg+xml",
                _ => null
            };

            if (mimeType == null) return null;

            byte[] imageBytes = File.ReadAllBytes(filePath);
            string base64String = Convert.ToBase64String(imageBytes);
            return $"data:{mimeType};base64,{base64String}";
        }

        public static async Task ConvertMarkdownToWord(string mdFilePath, string docxFilePath, string pandocPath)
        {
            string markdown = File.ReadAllText(mdFilePath);
            string processedMarkdown = PreprocessMarkdown(markdown);
            string tempMdPath = Path.GetTempFileName();
            File.WriteAllText(tempMdPath, processedMarkdown);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pandocPath,
                    Arguments = $"-s -o \"{docxFilePath}\" \"{tempMdPath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            File.Delete(tempMdPath);
        }
    }
}

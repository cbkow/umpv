using Markdown2Pdf.Options;
using Markdown2Pdf;
using PuppeteerSharp.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Helpers
{
    internal class PdfExportHelper
    {
        public static async Task ConvertMarkdownToPdfDirect(string mdFilePath, string pdfFilePath, string projectFolderName)
        {
            try
            {
                // Read the markdown file
                string markdown = await File.ReadAllTextAsync(mdFilePath);

                // Process the markdown to handle HTTP image URLs
                markdown = await ProcessMarkdownImagesForPdf(markdown, projectFolderName);

                // Create a temporary markdown file with the processed content
                string tempMdPath = Path.GetTempFileName();
                tempMdPath = Path.ChangeExtension(tempMdPath, ".md"); // Change extension to .md

                try
                {
                    // Write the processed markdown to the temp file
                    await File.WriteAllTextAsync(tempMdPath, markdown);

                    // Ensure the PDF file path is valid
                    string validPdfPath = SanitizeFilePath(pdfFilePath);
                    Debug.WriteLine($"Original PDF path: {pdfFilePath}");
                    Debug.WriteLine($"Sanitized PDF path: {validPdfPath}");
                    Debug.WriteLine($"Temp markdown file: {tempMdPath}");

                    // Get a clean document title
                    string documentTitle = Path.GetFileNameWithoutExtension(mdFilePath);
                    documentTitle = SanitizeFileName(documentTitle);

                    // Configure the PDF options using the correct properties
                    var pdfOptions = new Markdown2PdfOptions
                    {
                        // Document settings
                        DocumentTitle = documentTitle,
                        MetadataTitle = documentTitle,

                        // Use None theme so we can apply custom styling
                        Theme = Theme.None,

                        // Code highlighting
                        CodeHighlightTheme = CodeHighlightTheme.Github,

                        // Paper format
                        Format = PaperFormat.A4,
                        IsLandscape = false,
                        Scale = 1.0m,

                        // Margins - explicitly use the Markdown2Pdf version
                        MarginOptions = new Markdown2Pdf.Options.MarginOptions
                        {
                            Top = "12mm",
                            Bottom = "12mm",
                            Left = "10mm",
                            Right = "10mm"
                        },

                        // Custom CSS for styling
                        CustomHeadContent = $"<style>{GetExportCss()}</style>",

                        // Keep HTML for debugging if needed
                        KeepHtml = false
                    };

                    // Create converter with options
                    var converter = new Markdown2PdfConverter(pdfOptions);

                    // Convert to PDF using the temp markdown file path
                    await converter.Convert(tempMdPath, validPdfPath);

                    Debug.WriteLine($"Successfully converted markdown to PDF: {validPdfPath}");
                }
                finally
                {
                    // Clean up the temporary markdown file
                    try
                    {
                        if (File.Exists(tempMdPath))
                        {
                            File.Delete(tempMdPath);
                            Debug.WriteLine($"Cleaned up temp file: {tempMdPath}");
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        Debug.WriteLine($"Error cleaning up temp file: {cleanupEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting markdown to PDF: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private static string SanitizeFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Get the directory and filename separately
            string directory = Path.GetDirectoryName(path);
            string filename = Path.GetFileName(path);

            // Sanitize the filename
            string sanitizedFilename = SanitizeFileName(filename);

            // Combine back
            return Path.Combine(directory ?? "", sanitizedFilename);
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            // Remove invalid characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }

            // Also replace some common problematic characters
            fileName = fileName
                .Replace("#", "")
                .Replace("*", "")
                .Replace("?", "")
                .Replace("<", "")
                .Replace(">", "")
                .Replace("|", "")
                .Replace("\"", "")
                .Replace(":", "")
                .Trim();

            // Ensure it's not empty
            if (string.IsNullOrEmpty(fileName))
                fileName = "document";

            return fileName;
        }

        private static async Task<string> ProcessMarkdownImagesForPdf(string markdown, string projectFolderName)
        {
            try
            {
                // Pattern to find markdown images with HTTP URLs
                var imagePattern = @"!\[(.*?)\]\(http://192\.168\.40\.100:8723/([^)]+)\)";

                var matches = Regex.Matches(markdown, imagePattern);

                foreach (Match match in matches)
                {
                    try
                    {
                        string altText = match.Groups[1].Value;
                        string imagePath = match.Groups[2].Value;

                        // Convert HTTP URL back to network share path
                        string networkPath = $@"\\192.168.40.100\UnionNotes\{imagePath.Replace("/", "\\")}";

                        Debug.WriteLine($"Processing image for PDF: {networkPath}");
                        Debug.WriteLine($"Original HTTP path: http://192.168.40.100:8723/{imagePath}");
                        Debug.WriteLine($"Checking if file exists: {File.Exists(networkPath)}");

                        // Try to list the directory to see what's actually there
                        try
                        {
                            string directory = Path.GetDirectoryName(networkPath);
                            Debug.WriteLine($"Directory: {directory}");
                            if (Directory.Exists(directory))
                            {
                                var files = Directory.GetFiles(directory, "*.jpg").Take(5);
                                Debug.WriteLine($"Sample JPG files in directory: {string.Join(", ", files.Select(Path.GetFileName))}");
                            }
                            else
                            {
                                Debug.WriteLine($"Directory does not exist: {directory}");
                            }
                        }
                        catch (Exception dirEx)
                        {
                            Debug.WriteLine($"Error checking directory: {dirEx.Message}");
                        }

                        if (File.Exists(networkPath))
                        {
                            // Convert to base64 data URL
                            byte[] imageBytes = await File.ReadAllBytesAsync(networkPath);
                            string base64String = Convert.ToBase64String(imageBytes);

                            // Determine MIME type
                            string extension = Path.GetExtension(networkPath).ToLower();
                            string mimeType = extension switch
                            {
                                ".png" => "image/png",
                                ".jpg" => "image/jpeg",
                                ".jpeg" => "image/jpeg",
                                ".gif" => "image/gif",
                                ".svg" => "image/svg+xml",
                                ".webp" => "image/webp",
                                ".bmp" => "image/bmp",
                                _ => "image/jpeg"
                            };

                            string dataUrl = $"data:{mimeType};base64,{base64String}";

                            // Replace the HTTP URL with base64 data URL
                            string newImageMarkdown = $"![{altText}]({dataUrl})";
                            markdown = markdown.Replace(match.Value, newImageMarkdown);

                            Debug.WriteLine($"Converted image to base64 for PDF: {Path.GetFileName(networkPath)}");
                        }
                        else
                        {
                            Debug.WriteLine($"Image not found for PDF: {networkPath}");
                            // Leave the original markdown unchanged if image not found
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing image for PDF: {ex.Message}");
                    }
                }

                return markdown;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing markdown images for PDF: {ex.Message}");
                return markdown;
            }
        }

        private static string GetExportCss()
        {
            return @"
        /* Base styles matching your viewer */
        body {
            font-family: system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', 'Noto Sans', 'Liberation Sans', Arial, sans-serif;
            line-height: 1.4;
            color: #000000;
            margin: 0;
            padding: 0;
            background-color: white;
        }

        /* Headings */
        h1, h2, h3, h4, h5, h6 {
            font-family: system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', 'Noto Sans', 'Liberation Sans', Arial, sans-serif;
            color: black;
            margin-top: 2rem;
            margin-bottom: 1rem;
            font-weight: 400;
            line-height: 1.2;
        }

        h1 { font-size: 3rem; }
        h2 { font-size: 2.5rem; }
        h3 { font-size: 2rem; }
        h4 { font-size: 1.5rem; }
        h5 { font-size: 1.125rem; }
        h6 { font-size: 1rem; }

        /* Paragraphs */
        p {
            margin: 1rem 0;
            line-height: 1.5;
        }

        /* Code styling */
        code {
            background: #f5f5f5;
            color: #d73a49;
            padding: 0.2em 0.4em;
            font-family: 'JetBrains Mono', Menlo, Monaco, 'Courier New', Courier, monospace;
            font-size: 0.875em;
            border-radius: 3px;
        }

        pre {
            background: #f6f8fa;
            color: #24292f;
            padding: 1.5rem;
            overflow-x: auto;
            margin: 1.5rem 0;
            line-height: 1.6;
            border: 1px solid #d0d7de;
            border-radius: 6px;
        }

        pre code {
            background: transparent;
            color: inherit;
            padding: 0;
            font-size: 0.9em;
        }

        /* Lists */
        ul, ol {
            margin: 1rem 0;
            padding-left: 1.5rem;
            line-height: 1.6;
        }

        li {
            margin: 0.5rem 0;
            line-height: 1.6;
        }

        /* Tables */
        table {
            border-collapse: collapse;
            width: 100%;
            margin: 1rem 0;
            border: 1px solid #d0d7de;
            border-radius: 6px;
            overflow: hidden;
        }

        th, td {
            border: 1px solid #d0d7de;
            padding: 0.75rem;
            text-align: left;
            line-height: 1.5;
        }

        th {
            background-color: #f6f8fa;
            font-weight: 600;
        }

        tr:nth-child(even) {
            background-color: #f6f8fa;
        }

        /* Images */
        img {
            max-width: 100%;
            height: auto;
            border-radius: 6px;
            margin: 1rem 0;
            display: block;
        }

        /* Blockquotes */
        blockquote {
            border-left: 4px solid #dfe2e5;
            color: #656d76;
            margin: 1rem 0;
            padding: 0.5rem 1rem;
            background-color: #f6f8fa;
        }

        /* Horizontal rules */
        hr {
            border: none;
            border-top: 1px solid #d0d7de;
            margin: 2rem 0;
        }

        /* Print optimizations */
        @media print {
            body {
                font-size: 12pt;
                line-height: 1.4;
            }
            
            h1 { font-size: 24pt; }
            h2 { font-size: 20pt; }
            h3 { font-size: 16pt; }
            h4 { font-size: 14pt; }
            h5 { font-size: 12pt; }
            h6 { font-size: 12pt; }
        }
    ";
        }
    }
}

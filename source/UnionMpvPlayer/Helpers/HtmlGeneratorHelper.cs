using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Markdig;
using Microsoft.AspNetCore.StaticFiles;

namespace UnionMpvPlayer.Helpers
{
    public static class HtmlGeneratorHelper
    {
        public static string GenerateHtmlWithEmbeddedImagesAndCss(string markdownPath, bool forPreview = true)
        {
            return GenerateHtmlFromContent(File.Exists(markdownPath) ? File.ReadAllText(markdownPath) : LoadNothingMarkdown(), forPreview);
        }

        public static string GenerateHtmlFromMarkdownContent(string markdownContent, bool forPreview = true)
        {
            return GenerateHtmlFromContent(markdownContent, forPreview);
        }

        private static bool IsWebUrl(string url)
        {
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("://");
        }

        private static string PreprocessMarkdownLinks(string markdown)
        {
            markdown = Regex.Replace(
                markdown,
                @"\[([^\]]+)\]\(([^\)]+)\)",
                m =>
                {
                    var url = m.Groups[2].Value;
                    if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "http://" + url;
                    }
                    return IsWebUrl(url)
                        ? $"[{m.Groups[1].Value}]({url})"
                        : $"[{m.Groups[1].Value}]({SanitizeUrl(url)})";
                }
            );

            return markdown;
        }

        private static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            if (IsWebUrl(url)) return url;

            return url.Replace(" ", "%20");
        }

        private static string GenerateHtmlFromContent(string markdownContent, bool forPreview)
        {
            markdownContent = PreprocessMarkdownLinks(markdownContent);
            string htmlContent = MarkdownToHtml(markdownContent);
            string cssContent = LoadEmbeddedCss("convertMarkdownToHTML.css");

            string javascriptContent = $@"
            <script>
                {(forPreview ? @"
                document.addEventListener('contextmenu', event => event.preventDefault());
                document.addEventListener('click', function(event) {
                    if (event.target.tagName === 'A') {
                        event.preventDefault();
                        const url = event.target.href;
                        window.open(url, '_blank');
                    }
                });" : "")}
            </script>";

            htmlContent = $@"
            <html>
            <head>
                <style>{cssContent}</style>
                {javascriptContent}
            </head>
            <body class='markdown-body'>
                {htmlContent}
            </body>
            </html>";

            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var imageNodes = doc.DocumentNode.SelectNodes("//img[@src]");
            if (imageNodes != null)
            {
                foreach (var imgNode in imageNodes)
                {
                    string src = imgNode.GetAttributeValue("src", "");
                    src = Uri.UnescapeDataString(src);

                    if (src.StartsWith("file:///"))
                    {
                        src = new Uri(src).LocalPath;
                    }

                    if (File.Exists(src))
                    {
                        string ext = Path.GetExtension(src).ToLower();
                        if (ext == ".jpg" || ext == ".png" || ext == ".gif")
                        {
                            byte[] imgBytes = File.ReadAllBytes(src);
                            string base64Data = Convert.ToBase64String(imgBytes);
                            string mimeType = ext switch
                            {
                                ".jpg" => "image/jpeg",
                                ".png" => "image/png",
                                ".gif" => "image/gif",
                                _ => ""
                            };
                            imgNode.SetAttributeValue("src", $"data:{mimeType};base64,{base64Data}");
                        }
                    }
                    else
                    {
                        imgNode.SetAttributeValue("src", Uri.EscapeDataString(src));
                    }
                }
            }

            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (linkNodes != null)
            {
                foreach (var linkNode in linkNodes)
                {
                    string href = linkNode.GetAttributeValue("href", "");

                    if (IsWebUrl(href))
                    {
                        if (href.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                        {
                            href = "http://" + href;
                        }
                        linkNode.SetAttributeValue("href", href);
                        continue;
                    }

                    if (href.StartsWith("file:///"))
                    {
                        string filePath = new Uri(href).LocalPath;
                        if (File.Exists(filePath))
                        {
                            byte[] fileBytes = File.ReadAllBytes(filePath);
                            string base64Data = Convert.ToBase64String(fileBytes);
                            var provider = new FileExtensionContentTypeProvider();
                            provider.TryGetContentType(filePath, out string mimeType);
                            linkNode.SetAttributeValue("href", $"data:{mimeType};base64,{base64Data}");
                        }
                    }
                    else
                    {
                        linkNode.SetAttributeValue("href", SanitizeUrl(href));
                    }
                }
            }

            return doc.DocumentNode.OuterHtml;
        }

        private static string LoadNothingMarkdown()
        {
            var assembly = typeof(HtmlGeneratorHelper).Assembly;
            var resourcePath = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("notesViewer.Assets.nothing.md"));

            if (resourcePath != null)
            {
                using (var stream = assembly.GetManifestResourceStream(resourcePath))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }

            Console.WriteLine("nothing.md resource not found. Using a default message.");
            return "# Note not found\nPlease select an existing note or create a new one.";
        }

        private static string MarkdownToHtml(string markdown)
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            return Markdig.Markdown.ToHtml(markdown, pipeline);
        }

        private static string LoadEmbeddedCss(string resourceName)
        {
            var assembly = typeof(HtmlGeneratorHelper).Assembly;
            var resourcePath = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resourceName));

            if (resourcePath != null)
            {
                using (var stream = assembly.GetManifestResourceStream(resourcePath))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }

            Console.WriteLine("CSS resource not found.");
            return string.Empty;
        }
    }
}

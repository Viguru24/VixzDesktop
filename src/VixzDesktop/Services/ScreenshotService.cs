using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VixzDesktop.Services
{
    public static class ScreenshotService
    {
        public static string GetTargetDirectory(string? folderName = null)
        {
            if (!string.IsNullOrWhiteSpace(StorageService.Settings.CustomScreenshotPath) && Directory.Exists(StorageService.Settings.CustomScreenshotPath))
            {
                return StorageService.Settings.CustomScreenshotPath;
            }

            var folder = (folderName ?? StorageService.Settings.ActiveScreenshotFolder).Trim();
            if (string.IsNullOrWhiteSpace(folder)) folder = "Default";

            var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var targetDir = folder.Equals("Default", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(picturesDir, "Vixz")
                : Path.Combine(picturesDir, "Vixz", folder);

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            return targetDir;
        }

        public static async Task<string?> CaptureAndSaveAsync(
            WebView2? webView,
            FrameworkElement? visualFallback,
            string videoTitle,
            double positionSeconds,
            string? targetFolder = null)
        {
            try
            {
                var directory = GetTargetDirectory(targetFolder);
                var safeTitle = Regex.Replace(videoTitle, @"[^a-zA-Z0-9_-]", "_");
                if (safeTitle.Length > 30) safeTitle = safeTitle.Substring(0, 30);
                safeTitle = safeTitle.Trim('_');
                if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Video";

                var timeStr = TimeSpan.FromSeconds(positionSeconds);
                var formattedTime = $"{timeStr.Minutes:D2}m{timeStr.Seconds:D2}s";
                var dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"Vixz_{safeTitle}_{formattedTime}_{dateStamp}.jpg";
                var fullPath = Path.Combine(directory, fileName);

                // 1. Try Hardware-accelerated WebView2 Capture Preview
                if (webView?.CoreWebView2 != null)
                {
                    using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                    await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, fileStream);
                    return fullPath;
                }

                // 2. Fallback: WPF RenderTargetBitmap Visual capture
                if (visualFallback != null && visualFallback.ActualWidth > 0 && visualFallback.ActualHeight > 0)
                {
                    var rtb = new RenderTargetBitmap(
                        (int)visualFallback.ActualWidth,
                        (int)visualFallback.ActualHeight,
                        96, 96,
                        PixelFormats.Pbgra32
                    );
                    rtb.Render(visualFallback);

                    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    encoder.Frames.Add(BitmapFrame.Create(rtb));

                    using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                    encoder.Save(fs);
                    return fullPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Screenshot capture failed: {ex.Message}");
            }

            return null;
        }

        public static void OpenFolderInExplorer(string? folderName = null)
        {
            try
            {
                var dir = GetTargetDirectory(folderName);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}

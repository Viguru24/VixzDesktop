using System;
using System.IO;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace VixzDesktop.Services
{
    public class DownloadService
    {
        private static readonly YoutubeClient _client = new YoutubeClient();

        public static async Task<string> DownloadVideoAsync(string videoId, string title, IProgress<double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(videoId))
            {
                throw new ArgumentException("Video ID cannot be empty", nameof(videoId));
            }

            var manifest = await _client.Videos.Streams.GetManifestAsync(videoId);
            var streamInfo = manifest.GetMuxedStreams().GetWithHighestVideoQuality();
            if (streamInfo == null)
            {
                // Fallback to highest audio-only or video-only if no muxed
                streamInfo = manifest.GetMuxedStreams().FirstOrDefault();
                if (streamInfo == null)
                {
                    throw new Exception("No downloadable MP4 video stream found.");
                }
            }

            var downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Vixz");
            Directory.CreateDirectory(downloadsFolder);

            var cleanTitle = string.Concat(title.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = $"Video_{videoId}";
            if (cleanTitle.Length > 80) cleanTitle = cleanTitle.Substring(0, 80);

            var filePath = Path.Combine(downloadsFolder, $"{cleanTitle}.mp4");

            // Avoid collision
            int counter = 1;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(downloadsFolder, $"{cleanTitle}_{counter}.mp4");
                counter++;
            }

            await _client.Videos.Streams.DownloadAsync(streamInfo, filePath, progress);
            return filePath;
        }
    }
}

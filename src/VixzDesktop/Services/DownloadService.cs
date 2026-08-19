using System;
using System.IO;
using System.Linq;
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

            // 1. Try muxed streams (combined audio & video)
            IStreamInfo? streamInfo = manifest.GetMuxedStreams().GetWithHighestVideoQuality();

            if (streamInfo == null)
            {
                // 2. Fallback to any muxed stream
                streamInfo = manifest.GetMuxedStreams().FirstOrDefault();
            }

            if (streamInfo == null)
            {
                // 3. Fallback to video-only or audio-only
                streamInfo = (IStreamInfo?)manifest.GetVideoOnlyStreams().GetWithHighestVideoQuality() ??
                             (IStreamInfo?)manifest.GetAudioOnlyStreams().GetWithHighestBitrate() ??
                             manifest.Streams.FirstOrDefault();
            }

            if (streamInfo == null)
            {
                throw new Exception("No playable video or audio streams found for download.");
            }

            var downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Vixz");
            Directory.CreateDirectory(downloadsFolder);

            var cleanTitle = string.Concat(title.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = $"Video_{videoId}";
            if (cleanTitle.Length > 80) cleanTitle = cleanTitle.Substring(0, 80);

            var ext = streamInfo.Container.Name.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) ext = "mp4";

            var filePath = Path.Combine(downloadsFolder, $"{cleanTitle}.{ext}");

            // Avoid collision
            int counter = 1;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(downloadsFolder, $"{cleanTitle}_{counter}.{ext}");
                counter++;
            }

            await _client.Videos.Streams.DownloadAsync(streamInfo, filePath, progress);
            return filePath;
        }
    }
}

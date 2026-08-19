using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VixzDesktop.Models;

namespace VixzDesktop.Services
{
    public class AlgorithmSettings
    {
        public float CreatorWeight { get; set; } = 0.7f;
        public float DiscoveryRatio { get; set; } = 0.2f;
        public List<string> BlockedKeywords { get; set; } = new List<string>();
        public List<string> BoostedTopics { get; set; } = new List<string>();
    }

    public static class RecommendationEngine
    {
        private static readonly AlgorithmSettings DefaultSettings = new AlgorithmSettings();

        public static string GetRecommendationReason(
            VideoItem video,
            List<VideoItem> favorites,
            List<VideoItem> watchHistory,
            List<string> subscribedChannels)
        {
            var savedPos = StorageService.GetPlaybackPosition(video.Id);
            if (savedPos > 3) return "🕒 Continue Watching";
            if (video.IsFavorite) return "⭐ Saved Favorite";
            if (video.IsWatchLater) return "🔖 Saved Watch Later";

            var isSubscribed = subscribedChannels.Any(c =>
                c.Contains(video.ChannelTitle, StringComparison.OrdinalIgnoreCase) ||
                video.ChannelTitle.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (isSubscribed) return "💡 Subscribed Channel";

            var hasWatched = watchHistory.Any(v => v.ChannelTitle.Equals(video.ChannelTitle, StringComparison.OrdinalIgnoreCase));
            if (hasWatched) return "🔥 Channel You Enjoy";

            return "📈 Popular Recommendation";
        }

        public static List<VideoItem> ScoreAndRankVideos(
            List<VideoItem> videos,
            List<VideoItem> favorites,
            List<VideoItem> watchHistory,
            List<string>? subscribedChannels = null,
            AlgorithmSettings? settings = null)
        {
            if (videos == null || videos.Count == 0) return new List<VideoItem>();

            var currentSettings = settings ?? DefaultSettings;
            var subChannels = subscribedChannels ?? WillRyanProfileData.SubscribedChannels;

            var blockedLower = currentSettings.BlockedKeywords
                .Select(k => k.Trim().ToLowerInvariant())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();

            var boostedLower = currentSettings.BoostedTopics
                .Select(b => b.Trim().ToLowerInvariant())
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();

            var filteredVideos = videos.Where(v =>
            {
                var titleLower = v.Title.ToLowerInvariant();
                var chanLower = v.ChannelTitle.ToLowerInvariant();
                return !blockedLower.Any(blk => titleLower.Contains(blk) || chanLower.Contains(blk));
            }).ToList();

            // 1. Identify top favorite channels
            var topChannels = favorites.Select(f => f.ChannelTitle)
                .Concat(watchHistory.Select(w => w.ChannelTitle))
                .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // 2. Identify top watched keywords in titles
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "video", "with", "this", "that", "from", "2026", "youtube", "official"
            };

            var topKeywords = watchHistory
                .SelectMany(w => Regex.Split(w.Title.ToLowerInvariant(), @"\s+"))
                .Where(w => w.Length > 3 && !stopWords.Contains(w))
                .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var scoredList = new List<(VideoItem Video, float Score)>();

            foreach (var video in filteredVideos)
            {
                float score = 50.0f;

                // A. Favorite & Watch Later Boost
                if (video.IsFavorite) score += 40.0f;
                if (video.IsWatchLater) score += 25.0f;

                // B. User Boosted Topics & Creators
                var titleLower = video.Title.ToLowerInvariant();
                var chanLower = video.ChannelTitle.ToLowerInvariant();
                if (boostedLower.Any(bst => titleLower.Contains(bst) || chanLower.Contains(bst)))
                {
                    score += 90.0f;
                }

                // C. Subscribed Profile Channel Boost (+80 * CreatorWeight)
                var isSubscribedProfileChannel = subChannels.Any(c =>
                    c.Contains(video.ChannelTitle, StringComparison.OrdinalIgnoreCase) ||
                    video.ChannelTitle.Contains(c, StringComparison.OrdinalIgnoreCase));
                if (isSubscribedProfileChannel)
                {
                    score += 80.0f * currentSettings.CreatorWeight;
                }

                if (topChannels.TryGetValue(video.ChannelTitle, out int channelHits) && channelHits > 0)
                {
                    score += Math.Min(channelHits * 15.0f * currentSettings.CreatorWeight, 50.0f);
                }

                // D. Keyword & Subject Affinity
                var titleWords = Regex.Split(titleLower, @"\s+");
                int keywordHits = 0;
                foreach (var word in titleWords)
                {
                    if (topKeywords.TryGetValue(word, out int hits))
                    {
                        keywordHits += hits;
                    }
                }
                if (keywordHits > 0)
                {
                    score += Math.Min(keywordHits * 4.0f, 30.0f);
                }

                // E. Discovery Boost
                if (channelHits == 0 && !isSubscribedProfileChannel)
                {
                    score += currentSettings.DiscoveryRatio * 75.0f;
                }

                // F. Watched Progress / Deprioritize Completed Videos on Discovery Feed
                var savedPos = StorageService.GetPlaybackPosition(video.Id);
                if (savedPos > 3)
                {
                    score -= 60.0f;
                }

                video.AlgorithmScore = score;
                video.RecommendationReason = GetRecommendationReason(video, favorites, watchHistory, subChannels);

                scoredList.Add((video, score));
            }

            return scoredList
                .OrderByDescending(s => s.Score)
                .Select(s => s.Video)
                .ToList();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.ClosedCaptions;
using VixzDesktop.Models;

namespace VixzDesktop.Services
{
    public class VideoSummaryResult
    {
        public string VideoId { get; set; } = string.Empty;
        public string VideoTitle { get; set; } = string.Empty;
        public string ChannelTitle { get; set; } = string.Empty;
        public string Tldr { get; set; } = string.Empty;
        public List<string> KeyTakeaways { get; set; } = new List<string>();
        public List<TimestampChapter> Chapters { get; set; } = new List<TimestampChapter>();
        public bool HasTranscript { get; set; } = false;
    }

    public class TimestampChapter
    {
        public double Seconds { get; set; }
        public string TimeFormatted { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public enum AiCommandType
    {
        PlayVideo,
        Summarize,
        ControlSeek,
        ControlPause,
        ControlPlay,
        SetSleepTimer,
        SearchFeed,
        GeneralAnswer
    }

    public class AiCommandResult
    {
        public AiCommandType Type { get; set; } = AiCommandType.GeneralAnswer;
        public string ResponseMessage { get; set; } = string.Empty;
        public VideoItem? TargetVideo { get; set; }
        public VideoSummaryResult? Summary { get; set; }
        public double? SeekSeconds { get; set; }
        public int? TimerMinutes { get; set; }
        public string? SearchQuery { get; set; }
        public string? SpFilter { get; set; }
    }

    public class AiCopilotService
    {
        private static readonly YoutubeClient _client = new YoutubeClient();

        public static async Task<AiCommandResult> ProcessCommandAsync(string prompt, VideoItem? currentVideo)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return new AiCommandResult { ResponseMessage = "How can I help you today? Ask me to play a video, summarise what's playing, or control playback." };
            }

            var clean = prompt.Trim();
            var lower = clean.ToLowerInvariant();

            // 1. Summarize Video Command (Check first if user asked for summary)
            if (lower.Contains("summar") || lower.Contains("tl;dr") || lower.Contains("tldr") || 
                lower.Contains("key point") || lower.Contains("key takeaway") || lower.Contains("what is this video") ||
                lower.Contains("explain this video") || lower.Contains("notes"))
            {
                if (currentVideo == null)
                {
                    return new AiCommandResult
                    {
                        Type = AiCommandType.GeneralAnswer,
                        ResponseMessage = "⚠️ No video is currently playing. Start a video and ask me to summarise it!"
                    };
                }

                var summary = await GenerateSummaryAsync(currentVideo);
                return new AiCommandResult
                {
                    Type = AiCommandType.Summarize,
                    Summary = summary,
                    ResponseMessage = $"✨ Here is the AI summary for **{currentVideo.Title}**:"
                };
            }

            // 2. Play Latest Creator / Topic Video Command (e.g. "latest Dr Steve Turley", "play the latest Benny Johnson video", "Tucker Carlson today")
            bool isLatestQuery = lower.Contains("latest") || lower.Contains("newest") || lower.Contains("recent") || 
                                 lower.Contains("today") || lower.StartsWith("play") || lower.StartsWith("watch") || 
                                 lower.StartsWith("show") || lower.StartsWith("get");

            string candidateTarget = clean;
            candidateTarget = Regex.Replace(candidateTarget, @"\b(play|watch|show|get|the|latest|newest|recent|video|videos|by|from|of|today|now|please|i asked for|give me|find)\b", " ", RegexOptions.IgnoreCase).Trim();
            candidateTarget = Regex.Replace(candidateTarget, @"\s+", " ").Trim();

            if (!string.IsNullOrWhiteSpace(candidateTarget) && candidateTarget.Length >= 2 && candidateTarget != "this" && (isLatestQuery || candidateTarget.Split(' ').Length >= 2))
            {
                // Search YouTube strictly sorted by upload date (&sp=CAI%3D)
                var results = await YouTubeService.SearchVideosAsync(candidateTarget, 15, sortByUploadDate: true);
                if (results.Count > 0)
                {
                    // Sort strictly by newest upload
                    var sortedResults = YouTubeService.ApplyLocalFilters(results, null, null, "latest");
                    var topVideo = sortedResults.FirstOrDefault() ?? results[0];

                    return new AiCommandResult
                    {
                        Type = AiCommandType.PlayVideo,
                        TargetVideo = topVideo,
                        ResponseMessage = $"▶️ Playing the latest video from **{topVideo.ChannelTitle}**:\n*{topVideo.Title}* ({topVideo.UploadDateText})"
                    };
                }
                else
                {
                    return new AiCommandResult
                    {
                        Type = AiCommandType.SearchFeed,
                        SearchQuery = candidateTarget,
                        ResponseMessage = $"Couldn't find an exact instant match for \"{candidateTarget}\", showing search results."
                    };
                }
            }

            // 3. Sleep Timer Command (e.g. "set sleep timer for 30 minutes", "stop in 20 mins")
            var timerMatch = Regex.Match(lower, @"(?:sleep\s*timer|stop|turn\s*off|sleep)\s*(?:in|for)?\s*(\d+)\s*(?:min|minutes|m)?", RegexOptions.IgnoreCase);
            if (timerMatch.Success && int.TryParse(timerMatch.Groups[1].Value, out int minutes))
            {
                return new AiCommandResult
                {
                    Type = AiCommandType.SetSleepTimer,
                    TimerMinutes = minutes,
                    ResponseMessage = $"🌙 Sleep timer armed for **{minutes} minutes**."
                };
            }

            // 4. Seek / Fast-Forward / Rewind Commands (e.g. "skip 30 seconds", "go back 10s", "rewind 1 minute")
            var seekForwardMatch = Regex.Match(lower, @"(?:skip|forward|jump)\s*(?:ahead)?\s*(\d+)\s*(?:s|sec|seconds)?", RegexOptions.IgnoreCase);
            if (seekForwardMatch.Success && double.TryParse(seekForwardMatch.Groups[1].Value, out double fwdSec))
            {
                return new AiCommandResult
                {
                    Type = AiCommandType.ControlSeek,
                    SeekSeconds = fwdSec,
                    ResponseMessage = $"⏩ Skipped forward **+{fwdSec}s**."
                };
            }

            var seekBackMatch = Regex.Match(lower, @"(?:rewind|back|go back)\s*(\d+)\s*(?:s|sec|seconds)?", RegexOptions.IgnoreCase);
            if (seekBackMatch.Success && double.TryParse(seekBackMatch.Groups[1].Value, out double backSec))
            {
                return new AiCommandResult
                {
                    Type = AiCommandType.ControlSeek,
                    SeekSeconds = -backSec,
                    ResponseMessage = $"⏪ Jumped back **-{backSec}s**."
                };
            }

            // 5. Play / Pause Commands
            if (lower == "pause" || lower == "stop video" || lower == "pause video" || lower == "freeze")
            {
                return new AiCommandResult
                {
                    Type = AiCommandType.ControlPause,
                    ResponseMessage = "⏸️ Playback paused."
                };
            }

            if (lower == "play" || lower == "resume" || lower == "unpause" || lower == "continue")
            {
                return new AiCommandResult
                {
                    Type = AiCommandType.ControlPlay,
                    ResponseMessage = "▶️ Playback resumed."
                };
            }

            // 6. Generic Search Command (e.g. "find podcasts from today", "search AI tutorials")
            var searchMatch = Regex.Match(lower, @"(?:search|find|show me|look for)\s+(.+)", RegexOptions.IgnoreCase);
            if (searchMatch.Success)
            {
                var q = searchMatch.Groups[1].Value.Trim();
                string? sp = null;
                if (lower.Contains("today")) sp = "EgIIAg%3D%3D";
                else if (lower.Contains("this week")) sp = "EgIIAw%3D%3D";
                else if (lower.Contains("short")) sp = "EgQQARgB";
                else if (lower.Contains("long")) sp = "EgQQARgC";

                return new AiCommandResult
                {
                    Type = AiCommandType.SearchFeed,
                    SearchQuery = q,
                    SpFilter = sp,
                    ResponseMessage = $"🔍 Searching for **\"{q}\"**..."
                };
            }

            // 7. Conversational response / fallback
            return new AiCommandResult
            {
                Type = AiCommandType.GeneralAnswer,
                ResponseMessage = $"🤖 I understand commands like:\n• *\"Play the latest Benny Johnson video\"*\n• *\"Summarise this video\"*\n• *\"Skip 30 seconds\"*\n• *\"Set a 25 min sleep timer\"*\n• *\"Find breaking news from today\"*"
            };
        }

        public static async Task<VideoSummaryResult> GenerateSummaryAsync(VideoItem video)
        {
            var summary = new VideoSummaryResult
            {
                VideoId = video.Id,
                VideoTitle = video.Title,
                ChannelTitle = video.ChannelTitle
            };

            string rawTranscript = "";

            try
            {
                var trackManifest = await _client.Videos.ClosedCaptions.GetManifestAsync(video.Id);
                var trackInfo = trackManifest.TryGetByLanguage("en") ?? 
                                trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? 
                                trackManifest.Tracks.FirstOrDefault();

                if (trackInfo != null)
                {
                    var track = await _client.Videos.ClosedCaptions.GetAsync(trackInfo);
                    if (track != null && track.Captions.Count > 0)
                    {
                        summary.HasTranscript = true;
                        var sb = new StringBuilder();
                        var chapterInterval = Math.Max(60.0, (track.Captions.Last().Offset.TotalSeconds) / 6.0);
                        double nextChapterMark = 0;

                        foreach (var cap in track.Captions)
                        {
                            var text = cap.Text?.Replace("\n", " ").Trim() ?? "";
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            sb.Append(text).Append(" ");

                            if (cap.Offset.TotalSeconds >= nextChapterMark && summary.Chapters.Count < 6)
                            {
                                var cleanSnippet = text.Length > 40 ? text.Substring(0, 40) + "..." : text;
                                summary.Chapters.Add(new TimestampChapter
                                {
                                    Seconds = cap.Offset.TotalSeconds,
                                    TimeFormatted = FormatTime(cap.Offset),
                                    Title = cleanSnippet
                                });
                                nextChapterMark += chapterInterval;
                            }
                        }
                        rawTranscript = sb.ToString();
                    }
                }
            }
            catch { }

            // Extract description / video info fallback if no closed captions
            if (string.IsNullOrWhiteSpace(rawTranscript))
            {
                try
                {
                    var details = await _client.Videos.GetAsync(video.Id);
                    rawTranscript = $"{details.Title}. {details.Description}";
                }
                catch
                {
                    rawTranscript = $"{video.Title}. Video uploaded by {video.ChannelTitle}.";
                }
            }

            // Synthesize Executive Summary and Key Takeaways
            GenerateStructuredPoints(rawTranscript, video, summary);

            return summary;
        }

        private static void GenerateStructuredPoints(string text, VideoItem video, VideoSummaryResult summary)
        {
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => s.Length > 20 && !s.Contains("http") && !s.Contains("subscribe", StringComparison.OrdinalIgnoreCase))
                                .ToList();

            if (sentences.Count == 0)
            {
                summary.Tldr = $"A video titled **{video.Title}** published by **{video.ChannelTitle}**.";
                summary.KeyTakeaways.Add("Full transcript is unavailable for this upload.");
                summary.KeyTakeaways.Add($"Duration: {video.DurationText} • Published: {video.UploadDateText}");
                return;
            }

            // Executive TL;DR (Top informative sentences)
            var tldrSentences = sentences.Take(3).ToList();
            summary.Tldr = string.Join(". ", tldrSentences) + ".";

            // Key Takeaways
            var keyPoints = new List<string>();
            var step = Math.Max(1, sentences.Count / 5);
            for (int i = 0; i < sentences.Count && keyPoints.Count < 5; i += step)
            {
                var sentence = sentences[i];
                if (sentence.Length > 120) sentence = sentence.Substring(0, 117) + "...";
                keyPoints.Add(sentence);
            }

            if (keyPoints.Count == 0)
            {
                keyPoints.Add($"Key reporting and commentary from {video.ChannelTitle}.");
                keyPoints.Add($"Discussion covering: {video.Title}.");
            }

            summary.KeyTakeaways = keyPoints;
        }

        private static string FormatTime(TimeSpan ts)
        {
            return ts.Hours > 0
                ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}

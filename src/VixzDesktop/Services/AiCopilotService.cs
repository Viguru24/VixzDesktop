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
        public AiCommandType Type { get; set; }
        public string ResponseMessage { get; set; } = "";
        public VideoItem? TargetVideo { get; set; }
        public double? SeekSeconds { get; set; }
        public int? TimerMinutes { get; set; }
        public string SearchQuery { get; set; } = "";
        public string? SpFilter { get; set; }
        public VideoSummaryResult? Summary { get; set; }
    }

    public class VideoSummaryResult
    {
        public string VideoId { get; set; } = "";
        public string VideoTitle { get; set; } = "";
        public string ChannelTitle { get; set; } = "";
        public string Tldr { get; set; } = "";
        public List<string> KeyTakeaways { get; set; } = new List<string>();
        public List<TimestampChapter> Chapters { get; set; } = new List<TimestampChapter>();
        public bool HasTranscript { get; set; }
    }

    public class TimestampChapter
    {
        public double Seconds { get; set; }
        public string TimeFormatted { get; set; } = "";
        public string Title { get; set; } = "";
    }

    public class AiCopilotService
    {
        private static readonly YoutubeClient _client = new YoutubeClient();

        public static async Task<AiCommandResult> ProcessCommandAsync(string prompt, VideoItem? currentPlayingVideo)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return new AiCommandResult
                {
                    Type = AiCommandType.GeneralAnswer,
                    ResponseMessage = "Please type or speak a command (e.g., *\"Play the latest Benny Johnson video\"* or *\"Summarise this video\"*)."
                };
            }

            var lower = prompt.Trim().ToLowerInvariant();

            // 1. Summarize Command
            if (lower.Contains("summar") || lower.Contains("tl;dr") || lower.Contains("explain this video") || lower.Contains("tldr"))
            {
                if (currentPlayingVideo == null)
                {
                    return new AiCommandResult
                    {
                        Type = AiCommandType.GeneralAnswer,
                        ResponseMessage = "⚠️ No video is currently playing to summarize. Play a video first, then ask me to summarize it!"
                    };
                }

                var sum = await GenerateSummaryAsync(currentPlayingVideo);

                return new AiCommandResult
                {
                    Type = AiCommandType.Summarize,
                    TargetVideo = currentPlayingVideo,
                    Summary = sum,
                    ResponseMessage = $"✨ Summarizing **{currentPlayingVideo.Title}**..."
                };
            }

            // 2. Play Video Command (e.g. "play latest Benny Johnson", "play Tucker Carlson today")
            var playMatch = Regex.Match(lower, @"(?:play|watch|show|open|start)\s*(?:the)?\s*(?:latest|newest|today's)?\s*(.+)", RegexOptions.IgnoreCase);
            bool isLatestQuery = lower.Contains("latest") || lower.Contains("newest") || lower.Contains("today");

            string candidateTarget = "";
            if (playMatch.Success)
            {
                candidateTarget = playMatch.Groups[1].Value.Trim();
            }
            else if (isLatestQuery)
            {
                candidateTarget = prompt.Replace("latest", "", StringComparison.OrdinalIgnoreCase)
                                        .Replace("newest", "", StringComparison.OrdinalIgnoreCase)
                                        .Replace("today", "", StringComparison.OrdinalIgnoreCase)
                                        .Trim();
            }

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

            // 3. Sleep Timer Command
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

            // 4. Seek / Fast-Forward / Rewind Commands
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

            // 6. Generic Search Command
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

                // Robust caption track selection (manual English, auto-generated "a.en", or any English track)
                var trackInfo = trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals("en", StringComparison.OrdinalIgnoreCase)) ??
                                trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Contains("en", StringComparison.OrdinalIgnoreCase)) ??
                                trackManifest.Tracks.FirstOrDefault(t => t.Language.Name.Contains("English", StringComparison.OrdinalIgnoreCase)) ??
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
                                var cleanSnippet = text.Length > 45 ? text.Substring(0, 42) + "..." : text;
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
            if (string.IsNullOrWhiteSpace(text)) text = "";

            // 1. Strip ALL URLs, domain paths, social handles, and web fragments BEFORE sentence splitting
            text = Regex.Replace(text, @"https?://\S+", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b[\w\-]+\.(?:com|org|net|io|gov|edu|co|tv|app|me|be|yt|link)/\S*", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b[\w\-]+/(?:channel|c|user|watch|shorts)/[^\s.]*", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b[\w\-]+\.(?:com|org|net|io|gov|edu|co|tv|app|me|be|yt|link)\b", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"@[\w\-]+", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?:join|subscribe|membership|merch|sponsor|discount code|promo code)\S*", " ", RegexOptions.IgnoreCase);

            var rawSentences = text.Split(new[] { '.', '!', '?', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(s => s.Trim())
                                   .ToList();

            var cleanSentences = new List<string>();
            foreach (var s in rawSentences)
            {
                var lower = s.ToLowerInvariant();

                // Skip any residual URL fragments or noise
                if (lower.Contains("http") || lower.Contains("www.") || lower.Contains("com/") || 
                    lower.Contains("/channel/") || lower.Contains("@") || lower.Contains("subscribe") || 
                    lower.Contains("patreon") || lower.Contains("twitter") || lower.Contains("instagram") || 
                    lower.Contains("facebook") || lower.Contains("tiktok") || lower.Contains("discount") || 
                    lower.Contains("promo code") || lower.Contains("merch") || lower.Contains("sponsor") || 
                    lower.Contains("affiliate"))
                {
                    continue;
                }

                // Remove excessive whitespace
                var cleaned = Regex.Replace(s, @"\s+", " ").Trim();
                if (cleaned.Length < 18) continue;

                // Ignore exact video title duplicates
                if (cleaned.Equals(video.Title, StringComparison.OrdinalIgnoreCase)) continue;

                // Capitalize first letter
                if (char.IsLower(cleaned[0]))
                {
                    cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
                }

                if (!cleanSentences.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                {
                    cleanSentences.Add(cleaned);
                }
            }

            if (cleanSentences.Count == 0)
            {
                summary.Tldr = $"A video briefing titled **{video.Title}** presented by **{video.ChannelTitle}**.";
                summary.KeyTakeaways.Add($"Detailed analysis and commentary on: {video.Title}");
                summary.KeyTakeaways.Add($"Publisher: {video.ChannelTitle}");
                summary.KeyTakeaways.Add($"Upload Info: {video.UploadDateText} • Duration: {video.DurationText}");
                return;
            }

            // Executive TL;DR: 2 to 3 substantive sentences forming a clean summary paragraph
            var tldrList = cleanSentences.Take(3).ToList();
            summary.Tldr = string.Join(". ", tldrList);
            if (!summary.Tldr.EndsWith(".")) summary.Tldr += ".";

            // Key Takeaways: 3 to 5 distinct points distributed across the timeline
            var keyPoints = new List<string>();
            if (cleanSentences.Count <= 5)
            {
                keyPoints.AddRange(cleanSentences);
            }
            else
            {
                var step = cleanSentences.Count / 5.0;
                for (int i = 0; i < 5; i++)
                {
                    int index = Math.Min(cleanSentences.Count - 1, (int)(i * step));
                    var pt = cleanSentences[index];
                    if (pt.Length > 130) pt = pt.Substring(0, 127) + "...";
                    if (!keyPoints.Contains(pt)) keyPoints.Add(pt);
                }
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

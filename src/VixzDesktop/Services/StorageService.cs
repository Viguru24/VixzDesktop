using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using VixzDesktop.Models;

namespace VixzDesktop.Services
{
    public class AppSettings
    {
        public bool IsAutoplayEnabled { get; set; } = true;
        public string ActiveScreenshotFolder { get; set; } = "Default";
        public string? CustomScreenshotPath { get; set; } = null;
        public List<string> ScreenshotFolders { get; set; } = new List<string>
        {
            "Default",
            "Screenshots",
            "Favorites",
            "Recipes",
            "Notes",
            "Tutorials"
        };
        public List<VideoItem> Favorites { get; set; } = new List<VideoItem>();
        public List<VideoItem> WatchLater { get; set; } = new List<VideoItem>();
        public List<VideoItem> WatchHistory { get; set; } = new List<VideoItem>();
        public List<string> SubscribedChannels { get; set; } = new List<string>();
        public bool HasInitializedSubscriptions { get; set; } = false;
        public Dictionary<string, double> WatchPositions { get; set; } = new Dictionary<string, double>();
        public double Volume { get; set; } = 1.0;
        public string? GeminiApiKey { get; set; } = null;
    }

    public static class StorageService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VixzDesktop"
        );
        private static readonly string SettingsFile = Path.Combine(AppDataFolder, "settings.json");

        public static AppSettings Settings { get; private set; } = new AppSettings();

        static StorageService()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null)
                    {
                        Settings = settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public static void AddHistory(VideoItem video)
        {
            Settings.WatchHistory.RemoveAll(v => v.Id == video.Id);
            Settings.WatchHistory.Insert(0, video);
            if (Settings.WatchHistory.Count > 100)
            {
                Settings.WatchHistory.RemoveAt(Settings.WatchHistory.Count - 1);
            }
            Save();
        }

        public static void ToggleFavorite(VideoItem video)
        {
            var existing = Settings.Favorites.Find(v => v.Id == video.Id);
            if (existing != null)
            {
                Settings.Favorites.Remove(existing);
                video.IsFavorite = false;
            }
            else
            {
                video.IsFavorite = true;
                Settings.Favorites.Insert(0, video);
            }
            Save();
        }

        public static void ToggleWatchLater(VideoItem video)
        {
            var existing = Settings.WatchLater.Find(v => v.Id == video.Id);
            if (existing != null)
            {
                Settings.WatchLater.Remove(existing);
                video.IsWatchLater = false;
            }
            else
            {
                video.IsWatchLater = true;
                Settings.WatchLater.Insert(0, video);
            }
            Save();
        }

        public static void SavePlaybackPosition(string videoId, double positionSeconds)
        {
            if (string.IsNullOrWhiteSpace(videoId)) return;
            if (positionSeconds > 3)
            {
                Settings.WatchPositions[videoId] = positionSeconds;
                Save();
            }
        }

        public static double GetPlaybackPosition(string videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId)) return 0;
            if (Settings.WatchPositions.TryGetValue(videoId, out double pos))
            {
                return pos;
            }
            return 0;
        }
    }
}

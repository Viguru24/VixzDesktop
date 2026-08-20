using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using VixzDesktop.Models;
using VixzDesktop.Services;

namespace VixzDesktop
{
    public partial class MainWindow : Window
    {
        private List<VideoItem> _currentFeed = new List<VideoItem>();
        private List<VideoItem> _rawUnfilteredFeed = new List<VideoItem>();
        private VideoItem? _currentVideo = null;
        private int _currentVideoIndex = -1;
        private PopOutPlayerWindow? _popOutWindow = null;

        private DispatcherTimer? _sleepTimer = null;
        private int _sleepRemainingSeconds = 0;
        private int _lastSleepDurationMinutes = 30;

        private DispatcherTimer? _sponsorBlockTimer = null;
        private List<SponsorSegment> _activeSponsorSegments = new List<SponsorSegment>();

        private bool _isAlwaysOnTop = false;
        private bool _isCustomFullscreen = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateFolderUi();
            UpdateAutoplayUi();
            SubscribedChannelsList.ItemsSource = WillRyanProfileData.SubscribedChannels;
            SubscribersHeader.Text = $"👤 Subscriptions ({WillRyanProfileData.SubscribedChannels.Count})";
            await InitializeWebViewAsync();
            await LoadFeedAsync("Recommended Feed", () => YouTubeService.GetHomeFeedAsync());
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VixzDesktop");
                var userDataFolder = Path.Combine(appData, "WebView2Profile");

                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-features=PreloadMediaEngagementData");
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder, options: options);
                await VideoWebView.EnsureCoreWebView2Async(env);

                // Enable F12 DevTools and modern desktop capabilities
                VideoWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                VideoWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                VideoWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                VideoWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

                // Map virtual host https://vixz.app to local WebAssets for valid secure origin
                var webAssets = Path.Combine(appData, "WebAssets");
                Directory.CreateDirectory(webAssets);

                var playerHtmlPath = Path.Combine(webAssets, "player.html");
                var htmlContent = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; background: #000; overflow: hidden; }
        html, body { width: 100vw; height: 100vh; background: #000; }
        #player { width: 100vw; height: 100vh; position: absolute; top: 0; left: 0; border: none; }
    </style>
</head>
<body>
    <div id=""player""></div>
    <script>
        var tag = document.createElement('script');
        tag.src = 'https://www.youtube.com/iframe_api';
        var firstScriptTag = document.getElementsByTagName('script')[0];
        firstScriptTag.parentNode.insertBefore(tag, firstScriptTag);

        var player;
        var urlParams = new URLSearchParams(window.location.search);
        var currentVideoId = urlParams.get('v') || '';
        var startSec = parseFloat(urlParams.get('t') || '0') || 0;

        function onYouTubeIframeAPIReady() {
            if (!currentVideoId) return;
            player = new YT.Player('player', {
                videoId: currentVideoId,
                host: 'https://www.youtube.com',
                playerVars: {
                    'autoplay': 1,
                    'playsinline': 1,
                    'controls': 1,
                    'rel': 0,
                    'fs': 1,
                    'modestbranding': 1,
                    'iv_load_policy': 3,
                    'enablejsapi': 1,
                    'origin': window.location.origin,
                    'widget_referrer': window.location.origin,
                    'start': Math.floor(startSec)
                },
                events: {
                    'onReady': function(e) {
                        try { e.target.unMute(); } catch(err) {}
                        if (startSec > 3) {
                            try { e.target.seekTo(startSec, true); } catch(err) {}
                        }
                        try { e.target.playVideo(); } catch(err) {}
                        setTimeout(function() { try { e.target.unMute(); e.target.playVideo(); } catch(err) {} }, 250);
                        setTimeout(function() { try { e.target.unMute(); if (e.target.getPlayerState() !== 1) e.target.playVideo(); } catch(err) {} }, 750);
                    },
                    'onStateChange': onPlayerStateChange
                }
            });
        }

        var _autoPlayRetries = 0;
        function onPlayerStateChange(event) {
            if (event.data === 0) { // Ended
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage('VIDEO_ENDED');
                }
            }
            // Auto-resume if YouTube pauses during initial load/seek
            if (event.data === 2 || event.data === -1) {
                if (_autoPlayRetries < 3) {
                    _autoPlayRetries++;
                    setTimeout(function() {
                        try {
                            if (player && typeof player.playVideo === 'function' && player.getPlayerState() !== 1) {
                                player.playVideo();
                            }
                        } catch(e) {}
                    }, 300);
                }
            }
        }

        function loadVideo(vid, seekTime) {
            currentVideoId = vid;
            var targetSec = parseFloat(seekTime || '0') || 0;
            if (player && typeof player.loadVideoById === 'function') {
                player.loadVideoById({
                    videoId: vid,
                    startSeconds: targetSec
                });
            } else {
                window.location.href = 'https://vixz.app/player.html?v=' + vid + '&t=' + targetSec;
            }
        }

        var sponsorSegments = [];
        function setSponsorSegments(segs) {
            sponsorSegments = segs || [];
        }

        // High-precision SponsorBlock In-Video Ad Skipper (200ms tick)
        setInterval(() => {
            try {
                if (player && typeof player.getCurrentTime === 'function' && typeof player.getPlayerState === 'function') {
                    if (player.getPlayerState() === 1) { // Playing
                        var cur = player.getCurrentTime();
                        for (var i = 0; i < sponsorSegments.length; i++) {
                            var seg = sponsorSegments[i];
                            if (cur >= seg.start && cur < (seg.end - 0.25)) {
                                player.seekTo(seg.end + 0.1, true);
                                if (window.chrome && window.chrome.webview) {
                                    window.chrome.webview.postMessage('SPONSOR_SKIPPED:' + (seg.category || 'sponsor'));
                                }
                                break;
                            }
                        }
                    }
                }
            } catch(e) {}
        }, 200);

        // Progress memory tracker - sends current position every 1.5s
        setInterval(() => {
            try {
                if (player && typeof player.getCurrentTime === 'function' && typeof player.getPlayerState === 'function') {
                    if (player.getPlayerState() === 1) { // Playing
                        var cur = player.getCurrentTime();
                        if (cur > 2 && currentVideoId && window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage('POS:' + currentVideoId + ':' + cur.toFixed(1));
                        }
                    }
                }
            } catch(e) {}
        }, 1500);

        function stopVideo() {
            try {
                if (player && typeof player.stopVideo === 'function') player.stopVideo();
                if (player && typeof player.pauseVideo === 'function') player.pauseVideo();
            } catch(e) {}
        }

        function unMuteVideo() {
            try {
                if (player && typeof player.unMute === 'function') player.unMute();
            } catch(e) {}
        }

        function pauseVideo() {
            if (player && typeof player.pauseVideo === 'function') {
                player.pauseVideo();
            }
        }

        function playVideo() {
            if (player && typeof player.playVideo === 'function') {
                try { player.unMute(); } catch(e) {}
                player.playVideo();
            }
        }

        function togglePlay() {
            if (!player || typeof player.getPlayerState !== 'function') return;
            var s = player.getPlayerState();
            if (s === 1) pauseVideo();
            else playVideo();
        }

        function seek(sec) {
            if (!player || typeof player.getCurrentTime !== 'function') return;
            var cur = player.getCurrentTime();
            player.seekTo(cur + sec, true);
        }

        function seekTo(sec) {
            if (!player || typeof player.seekTo !== 'function') return;
            player.seekTo(sec, true);
        }

        function toggleMute() {
            if (!player || typeof player.isMuted !== 'function') return;
            if (player.isMuted()) player.unMute();
            else player.mute();
        }

        function getCurrentTime() {
            if (!player || typeof player.getCurrentTime !== 'function') return 0;
            return player.getCurrentTime() || 0;
        }
    </script>
</body>
</html>";
                File.WriteAllText(playerHtmlPath, htmlContent);

                VideoWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "vixz.app",
                    webAssets,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // Intercept and mock ad requests with 200 OK to prevent ERR_CONNECTION_REFUSED
                VideoWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

                VideoWebView.CoreWebView2.WebResourceRequested += (s, args) =>
                {
                    try
                    {
                        var uri = args.Request.Uri.ToLowerInvariant();
                        if (uri.Contains("doubleclick") || uri.Contains("googleads") || uri.Contains("/pagead/") || uri.Contains("ad_status") || uri.Contains("favicon.ico") || uri.Contains("viewthroughconversion"))
                        {
                            string origin = "*";
                            try
                            {
                                if (args.Request.Headers.Contains("Origin"))
                                {
                                    origin = args.Request.Headers.GetHeader("Origin");
                                    if (string.IsNullOrWhiteSpace(origin)) origin = "*";
                                }
                            }
                            catch { }

                            string contentType = "text/plain";
                            byte[] bodyBytes = Array.Empty<byte>();

                            if (uri.Contains(".js") || uri.Contains("ad_status"))
                            {
                                contentType = "application/javascript";
                                bodyBytes = System.Text.Encoding.UTF8.GetBytes("/* mock ad script */\nwindow.google_ad_status = 1;\n");
                            }
                            else if (uri.Contains("favicon.ico"))
                            {
                                contentType = "image/x-icon";
                                bodyBytes = new byte[] { 0, 0, 1, 0, 1, 0, 1, 1, 0, 0, 1, 0, 32, 0, 68, 0, 0, 0, 22, 0, 0, 0, 40, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                            }
                            else
                            {
                                contentType = "application/json";
                                bodyBytes = System.Text.Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"id\":\"0\"}");
                            }

                            var headers = $"Content-Type: {contentType}\r\nAccess-Control-Allow-Origin: {origin}\r\nAccess-Control-Allow-Credentials: true\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS, PUT, DELETE\r\nAccess-Control-Allow-Headers: *";
                            var emptyStream = new MemoryStream(bodyBytes);
                            args.Response = VideoWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                                emptyStream,
                                200,
                                "OK",
                                headers
                            );
                        }
                    }
                    catch { }
                };

                VideoWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                VideoWebView.KeyDown += Window_KeyDown;

                // Intercept 'More videos' and external links so they play inside Vixz instead of opening external browsers
                VideoWebView.CoreWebView2.NewWindowRequested += async (s, args) =>
                {
                    args.Handled = true;
                    var vid = ExtractYouTubeVideoId(args.Uri);
                    if (!string.IsNullOrEmpty(vid))
                    {
                        var video = await YouTubeService.GetVideoDetailsAsync(vid) ?? new VideoItem
                        {
                            Id = vid,
                            Title = "YouTube Video",
                            ChannelTitle = "YouTube",
                            ThumbnailUrl = $"https://i.ytimg.com/vi/{vid}/hqdefault.jpg"
                        };
                        await PlayVideoAsync(video);
                    }
                };

                VideoWebView.CoreWebView2.NavigationStarting += async (s, args) =>
                {
                    if (args.Uri.StartsWith("https://vixz.app", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    args.Cancel = true;
                    var vid = ExtractYouTubeVideoId(args.Uri);
                    if (!string.IsNullOrEmpty(vid))
                    {
                        var video = await YouTubeService.GetVideoDetailsAsync(vid) ?? new VideoItem
                        {
                            Id = vid,
                            Title = "YouTube Video",
                            ChannelTitle = "YouTube",
                            ThumbnailUrl = $"https://i.ytimg.com/vi/{vid}/hqdefault.jpg"
                        };
                        await PlayVideoAsync(video);
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 init error: {ex.Message}");
            }
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(msg)) return;

                if (msg == "VIDEO_ENDED")
                {
                    if (StorageService.Settings.IsAutoplayEnabled)
                    {
                        PlayNextVideo();
                    }
                }
                else if (msg.StartsWith("SPONSOR_SKIPPED:"))
                {
                    var cat = msg.Substring("SPONSOR_SKIPPED:".Length);
                    ShowToast($"⏭️ Skipped {cat} (in-video ad)");
                }
                else if (msg.StartsWith("POS:"))
                {
                    var parts = msg.Split(':');
                    if (parts.Length == 3 && double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pos))
                    {
                        StorageService.SavePlaybackPosition(parts[1], pos);
                    }
                }
            }
            catch { }
        }

        #region Feed & Navigation

        private async Task LoadFeedAsync(string title, Func<Task<List<VideoItem>>> fetcher)
        {
            FeedTitleText.Text = title;
            LoadingSpinner.Visibility = Visibility.Visible;
            VideoItemsControl.ItemsSource = null;

            try
            {
                _rawUnfilteredFeed = await fetcher();
                ApplyCurrentFilters();
            }
            catch (Exception ex)
            {
                ShowToast($"Error loading feed: {ex.Message}");
            }
            finally
            {
                LoadingSpinner.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyCurrentFilters()
        {
            if (DateFilterCombo == null || DurationFilterCombo == null || SortByFilterCombo == null || VideoItemsControl == null)
            {
                return;
            }

            if (_rawUnfilteredFeed == null || _rawUnfilteredFeed.Count == 0)
            {
                _currentFeed = new List<VideoItem>();
                VideoItemsControl.ItemsSource = _currentFeed;
                return;
            }

            var dateTag = (DateFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var durationTag = (DurationFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var sortByTag = (SortByFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            _currentFeed = YouTubeService.ApplyLocalFilters(_rawUnfilteredFeed, dateTag, durationTag, sortByTag);
            VideoItemsControl.ItemsSource = _currentFeed;
        }

        private void FilterOrSort_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyCurrentFilters();
        }

        private async void ApplyFiltersBtn_Click(object sender, RoutedEventArgs e)
        {
            var query = SearchBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                await PerformSearchWithFiltersAsync();
                return;
            }

            var dateTag = (DateFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var durationTag = (DurationFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var sortByTag = (SortByFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            if (!string.IsNullOrWhiteSpace(dateTag) || !string.IsNullOrWhiteSpace(durationTag) || sortByTag == "latest" || sortByTag == "views")
            {
                LoadingSpinner.Visibility = Visibility.Visible;
                SwitchToFeedView();
                var label = dateTag == "today" ? "Today's Newest Videos" : "Filtered Feed";
                FeedTitleText.Text = $"⚡ {label}";

                var results = await YouTubeService.GetDeepFilteredFeedAsync(dateTag, durationTag, sortByTag);
                _rawUnfilteredFeed = results;
                _currentFeed = results;
                VideoItemsControl.ItemsSource = _currentFeed;
                LoadingSpinner.Visibility = Visibility.Collapsed;
                ShowToast($"⚡ Found {_currentFeed.Count} videos matching filters");
            }
            else
            {
                ApplyCurrentFilters();
            }
        }

        private void ResetFiltersBtn_Click(object sender, RoutedEventArgs e)
        {
            DateFilterCombo.SelectedIndex = 0;
            DurationFilterCombo.SelectedIndex = 0;
            SortByFilterCombo.SelectedIndex = 0;
            _currentFeed = _rawUnfilteredFeed.ToList();
            VideoItemsControl.ItemsSource = _currentFeed;
            ShowToast("Filters reset");
        }

        private async void NavHome_Click(object sender, RoutedEventArgs e)
        {
            SwitchToFeedView();
            await LoadFeedAsync("Recommended Feed", () => YouTubeService.GetHomeFeedAsync());
        }

        private async void NavSubscriptions_Click(object sender, RoutedEventArgs e)
        {
            SwitchToFeedView();
            await LoadFeedAsync("🔔 Subscriptions Feed", () => YouTubeService.GetSubscribedFeedAsync());
        }

        private async void ChannelFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string channelName)
            {
                SwitchToFeedView();
                await LoadFeedAsync($"🔔 {channelName}", () => YouTubeService.GetSubscribedFeedAsync(channelName));
            }
        }

        private async void NavTrending_Click(object sender, RoutedEventArgs e)
        {
            SwitchToFeedView();
            await LoadFeedAsync("🔥 Trending Videos", () => YouTubeService.SearchVideosAsync("Trending Worldwide", 30));
        }

        private void NavFavorites_Click(object sender, RoutedEventArgs e)
        {
            SwitchToFeedView();
            FeedTitleText.Text = "⭐ Favorite Videos";
            _rawUnfilteredFeed = StorageService.Settings.Favorites.ToList();
            ApplyCurrentFilters();
        }

        private void NavWatchLater_Click(object sender, RoutedEventArgs e)
        {
            SwitchToFeedView();
            FeedTitleText.Text = "🕒 Watch Later Queue";
            _rawUnfilteredFeed = StorageService.Settings.WatchLater.ToList();
            ApplyCurrentFilters();
        }

        private void NavHistory_Click(object sender, RoutedEventArgs e)
        {
            SwitchToFeedView();
            FeedTitleText.Text = "📜 Watch History";
            _rawUnfilteredFeed = StorageService.Settings.WatchHistory.ToList();
            ApplyCurrentFilters();
        }

        private void SwitchToFeedView()
        {
            PlayerView.Visibility = Visibility.Collapsed;
            FeedView.Visibility = Visibility.Visible;
            _sponsorBlockTimer?.Stop();
        }

        private void SwitchToPlayerView()
        {
            FeedView.Visibility = Visibility.Collapsed;
            PlayerView.Visibility = Visibility.Visible;
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await PerformSearchAsync();
            }
        }

        private async Task PerformSearchAsync()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;

            // Direct YouTube URL or Video ID detection -> play instantly!
            var extractedId = ExtractYouTubeVideoId(query);
            if (!string.IsNullOrEmpty(extractedId))
            {
                var video = await YouTubeService.GetVideoDetailsAsync(extractedId) ?? new VideoItem
                {
                    Id = extractedId,
                    Title = "YouTube Video (" + extractedId + ")",
                    ChannelTitle = "YouTube",
                    ThumbnailUrl = $"https://i.ytimg.com/vi/{extractedId}/hqdefault.jpg"
                };
                await PlayVideoAsync(video);
                return;
            }

            await PerformSearchWithFiltersAsync();
        }

        private async Task PerformSearchWithFiltersAsync()
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;

            var dateTag = (DateFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var durationTag = (DurationFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var sortByTag = (SortByFilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            string? spParam = null;
            if (sortByTag == "latest") spParam = "CAI%3D";
            else if (sortByTag == "views") spParam = "CAM%3D";
            else if (dateTag == "today") spParam = "EgIIAg%3D%3D";
            else if (dateTag == "week") spParam = "EgIIAw%3D%3D";
            else if (dateTag == "month") spParam = "EgIIBA%3D%3D";
            else if (durationTag == "short") spParam = "EgQQARgB";
            else if (durationTag == "medium") spParam = "EgQQARgD";
            else if (durationTag == "long") spParam = "EgQQARgC";

            LoadingSpinner.Visibility = Visibility.Visible;
            SwitchToFeedView();
            FeedTitleText.Text = $"🔍 Search: \"{query}\"";

            var results = await YouTubeService.SearchVideosAsync(query, 35, spFilter: spParam);
            _rawUnfilteredFeed = results;
            _currentFeed = YouTubeService.ApplyLocalFilters(results, dateTag, durationTag, sortByTag);
            VideoItemsControl.ItemsSource = _currentFeed;
            LoadingSpinner.Visibility = Visibility.Collapsed;
        }

        public static string? ExtractYouTubeVideoId(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            input = input.Trim();

            // 1. Check if it's already an 11-character video ID
            if (System.Text.RegularExpressions.Regex.IsMatch(input, @"^[a-zA-Z0-9_-]{11}$"))
            {
                return input;
            }

            // 2. youtu.be/ID (e.g. https://youtu.be/u0JVCVOIePo?si=...)
            var matchShort = System.Text.RegularExpressions.Regex.Match(input, @"youtu\.be\/([a-zA-Z0-9_-]{11})");
            if (matchShort.Success)
            {
                return matchShort.Groups[1].Value;
            }

            // 3. youtube.com/watch?v=ID or /embed/ID or /shorts/ID or /live/ID
            var matchStandard = System.Text.RegularExpressions.Regex.Match(input, @"(?:v=|embed\/|shorts\/|live\/)([a-zA-Z0-9_-]{11})");
            if (matchStandard.Success)
            {
                return matchStandard.Groups[1].Value;
            }

            return null;
        }

        #endregion

        #region Video Player

        private async void VideoCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is VideoItem video)
            {
                _currentVideoIndex = _currentFeed.IndexOf(video);
                await PlayVideoAsync(video);
            }
        }

        private async Task PlayVideoAsync(VideoItem video, double? resumePos = null)
        {
            _currentVideo = video;
            StorageService.AddHistory(video);

            CurrentVideoTitle.Text = video.Title;
            CurrentVideoChannel.Text = video.ChannelTitle;
            CurrentVideoDate.Text = !string.IsNullOrWhiteSpace(video.UploadDateText) ? $" • {video.UploadDateText}" : "";
            CurrentVideoViews.Text = !string.IsNullOrWhiteSpace(video.ViewCountText) ? $" • {video.ViewCountText}" : "";

            // If date is missing, fetch full details asynchronously
            if (string.IsNullOrWhiteSpace(video.UploadDateText))
            {
                _ = Task.Run(async () =>
                {
                    var details = await YouTubeService.GetVideoDetailsAsync(video.Id);
                    if (details != null && !string.IsNullOrWhiteSpace(details.UploadDateText))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_currentVideo?.Id == video.Id)
                            {
                                video.UploadDateText = details.UploadDateText;
                                CurrentVideoDate.Text = $" • {details.UploadDateText}";
                            }
                        });
                    }
                });
            }

            FavBtn.Foreground = video.IsFavorite ? (System.Windows.Media.Brush)FindResource("AccentGold") : System.Windows.Media.Brushes.White;
            WatchLaterBtn.Foreground = video.IsWatchLater ? (System.Windows.Media.Brush)FindResource("AccentRed") : System.Windows.Media.Brushes.White;

            FeedView.Visibility = Visibility.Collapsed;
            PlayerView.Visibility = Visibility.Visible;

            if (VideoWebView.CoreWebView2 == null)
            {
                await InitializeWebViewAsync();
            }

            // Load SponsorBlock segments in background
            _activeSponsorSegments = await SponsorBlockService.GetSegmentsAsync(video.Id);

            var savedPos = resumePos ?? StorageService.GetPlaybackPosition(video.Id);
            if (savedPos > 3)
            {
                var ts = TimeSpan.FromSeconds(savedPos);
                var timeFormatted = ts.Hours > 0 ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
                ShowToast($"▶ Resuming from {timeFormatted}");
            }

            var currentSrc = VideoWebView.Source?.ToString() ?? "";
            if (currentSrc.Contains("vixz.app/player.html"))
            {
                await VideoWebView.ExecuteScriptAsync($"loadVideo('{video.Id}', {savedPos.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            }
            else
            {
                VideoWebView.CoreWebView2?.Navigate($"https://vixz.app/player.html?v={video.Id}&t={savedPos.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            // Inject SponsorBlock segments directly into player engine
            if (_activeSponsorSegments != null && _activeSponsorSegments.Count > 0)
            {
                var segsList = _activeSponsorSegments.Select(s => new { start = s.StartTime, end = s.EndTime, category = s.Category }).ToList();
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(segsList);
                await VideoWebView.ExecuteScriptAsync($"setSponsorSegments({json})");
            }
            else
            {
                await VideoWebView.ExecuteScriptAsync("setSponsorSegments([])");
            }

            // Start SponsorBlock monitor
            StartSponsorBlockMonitor();
        }

        private async void SeekMinus10_Click(object sender, RoutedEventArgs e)
        {
            await VideoWebView.ExecuteScriptAsync("seek(-10);");
            ShowToast("⏪ -10s");
        }

        private async void SeekMinus5_Click(object sender, RoutedEventArgs e)
        {
            await VideoWebView.ExecuteScriptAsync("seek(-5);");
            ShowToast("⏪ -5s");
        }

        private async void SeekPlus5_Click(object sender, RoutedEventArgs e)
        {
            await VideoWebView.ExecuteScriptAsync("seek(5);");
            ShowToast("⏩ +5s");
        }

        private async void SeekPlus10_Click(object sender, RoutedEventArgs e)
        {
            await VideoWebView.ExecuteScriptAsync("seek(10);");
            ShowToast("⏩ +10s");
        }

        private async void PopOutPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVideo == null)
            {
                ShowToast("No video currently loaded");
                return;
            }

            double curSec = 0;
            try
            {
                var timeStr = await VideoWebView.ExecuteScriptAsync("getCurrentTime()");
                double.TryParse(timeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out curSec);
                await VideoWebView.ExecuteScriptAsync("pauseVideo();");
            }
            catch { }

            _popOutWindow?.Close();
            _popOutWindow = new PopOutPlayerWindow(this, _currentVideo, curSec);

            var workArea = SystemParameters.WorkArea;
            _popOutWindow.Left = workArea.Right - _popOutWindow.Width - 30;
            _popOutWindow.Top = workArea.Bottom - _popOutWindow.Height - 30;

            _popOutWindow.Show();
            SwitchToFeedView();
            ShowToast("⧉ Floating Pop-Out Player Launched (Always on Top)");
        }

        public void ReturnFromPopOut(VideoItem video, double positionSeconds)
        {
            _popOutWindow = null;
            VideoWebView.Visibility = Visibility.Visible;
            SwitchToPlayerView();
            _ = PlayVideoAsync(video, positionSeconds);
            ShowToast($"⧉ Restored to main player at {TimeSpan.FromSeconds(positionSeconds):mm\\:ss}");
        }

        private void StartSponsorBlockMonitor()
        {
            _sponsorBlockTimer?.Stop();
            if (_activeSponsorSegments == null || _activeSponsorSegments.Count == 0) return;

            _sponsorBlockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _sponsorBlockTimer.Tick += async (s, e) =>
            {
                try
                {
                    if (VideoWebView.CoreWebView2 != null && _activeSponsorSegments.Count > 0)
                    {
                        var timeStr = await VideoWebView.ExecuteScriptAsync("getCurrentTime()");
                        if (double.TryParse(timeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double currentSec))
                        {
                            var segment = _activeSponsorSegments.FirstOrDefault(seg => currentSec >= seg.StartTime && currentSec < (seg.EndTime - 0.5));
                            if (segment != null)
                            {
                                await VideoWebView.ExecuteScriptAsync($"seek({segment.EndTime - currentSec + 0.1})");
                                ShowToast($"⏭️ Skipped {segment.Category}");
                            }
                        }
                    }
                }
                catch { }
            };
            _sponsorBlockTimer.Start();
        }

        public void PlayNextVideo()
        {
            if (_currentFeed.Count == 0) return;

            _currentVideoIndex++;
            if (_currentVideoIndex >= _currentFeed.Count)
            {
                _currentVideoIndex = 0;
            }

            var next = _currentFeed[_currentVideoIndex];
            _ = PlayVideoAsync(next);
            ShowToast("Autoplay: Playing Next Video ⏭️");
        }

        public void PlayPreviousVideo()
        {
            if (_currentFeed.Count == 0) return;

            _currentVideoIndex--;
            if (_currentVideoIndex < 0)
            {
                _currentVideoIndex = _currentFeed.Count - 1;
            }

            var prev = _currentFeed[_currentVideoIndex];
            _ = PlayVideoAsync(prev);
            ShowToast("Playing Previous Video ⏮️");
        }

        private void BackToFeed_Click(object sender, RoutedEventArgs e)
        {
            SwitchToFeedView();
        }

        private void NextVideoBtn_Click(object sender, RoutedEventArgs e)
        {
            PlayNextVideo();
        }

        private void FavBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVideo != null)
            {
                StorageService.ToggleFavorite(_currentVideo);
                FavBtn.Foreground = _currentVideo.IsFavorite ? (System.Windows.Media.Brush)FindResource("AccentGold") : System.Windows.Media.Brushes.White;
                ShowToast(_currentVideo.IsFavorite ? "Added to Favorites ⭐" : "Removed from Favorites");
            }
        }

        private void WatchLaterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentVideo != null)
            {
                StorageService.ToggleWatchLater(_currentVideo);
                WatchLaterBtn.Foreground = _currentVideo.IsWatchLater ? (System.Windows.Media.Brush)FindResource("AccentRed") : System.Windows.Media.Brushes.White;
                ShowToast(_currentVideo.IsWatchLater ? "Added to Watch Later 🕒" : "Removed from Watch Later");
            }
        }

        private void AutoplayBtn_Click(object sender, RoutedEventArgs e)
        {
            StorageService.Settings.IsAutoplayEnabled = !StorageService.Settings.IsAutoplayEnabled;
            StorageService.Save();
            UpdateAutoplayUi();
            ShowToast(StorageService.Settings.IsAutoplayEnabled ? "▶️ Autoplay is ON" : "⏸️ Autoplay is OFF");
        }

        private void UpdateAutoplayUi()
        {
            if (AutoplayBtn != null)
            {
                AutoplayBtn.Content = StorageService.Settings.IsAutoplayEnabled ? "▶️" : "⏸️";
                AutoplayBtn.Foreground = StorageService.Settings.IsAutoplayEnabled ? (System.Windows.Media.Brush)FindResource("AccentGold") : System.Windows.Media.Brushes.Gray;
            }
        }

        #endregion

        #region Video Download & Screenshot

        private async void DownloadVideoBtn_Click(object sender, RoutedEventArgs e)
        {
            await DownloadCurrentVideoAsync();
        }

        private bool _isDownloading = false;

        private async Task DownloadCurrentVideoAsync()
        {
            if (_currentVideo == null)
            {
                ShowToast("⚠️ No video is currently playing");
                return;
            }

            if (_isDownloading)
            {
                ShowToast("⏳ Download already in progress...");
                return;
            }

            _isDownloading = true;
            DownloadVideoBtn.Foreground = (System.Windows.Media.Brush)FindResource("AccentGold");
            ShowToast($"📥 Fetching video streams...");

            try
            {
                var progressHandler = new Progress<double>(p =>
                {
                    var percent = (int)(p * 100);
                    if (percent % 10 == 0 || percent == 100)
                    {
                        Dispatcher.Invoke(() => ShowToast($"📥 Downloading MP4: {percent}%"));
                    }
                });

                var filePath = await DownloadService.DownloadVideoAsync(_currentVideo.Id, _currentVideo.Title, progressHandler);
                ShowToast($"✅ Download Complete!");

                var result = MessageBox.Show($"Video downloaded successfully!\n\nSaved to: {filePath}\n\nWould you like to open the folder?", "Download Complete", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                ShowToast($"⚠️ Download failed: {ex.Message}");
            }
            finally
            {
                _isDownloading = false;
                DownloadVideoBtn.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private async void ScreenshotBtn_Click(object sender, RoutedEventArgs e)
        {
            await CaptureScreenshotAsync();
        }

        private async Task CaptureScreenshotAsync()
        {
            // Shutter Flash Animation
            TriggerShutterFlash();

            double currentSec = 0;
            try
            {
                var timeStr = await VideoWebView.ExecuteScriptAsync("getCurrentTime()");
                double.TryParse(timeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out currentSec);
            }
            catch { }

            var path = await ScreenshotService.CaptureAndSaveAsync(
                VideoWebView,
                PlayerContainer,
                _currentVideo?.Title ?? "Video",
                currentSec,
                StorageService.Settings.ActiveScreenshotFolder
            );

            if (path != null)
            {
                var folderName = StorageService.Settings.ActiveScreenshotFolder;
                var folderDisplay = folderName == "Default" ? @"Pictures\Vixz" : $@"Pictures\Vixz\{folderName}";
                ShowToast($"📸 Saved to {folderDisplay}");
            }
            else
            {
                ShowToast("⚠️ Failed to capture screenshot");
            }
        }

        private void TriggerShutterFlash()
        {
            var anim = new DoubleAnimation(0.85, 0.0, TimeSpan.FromMilliseconds(200));
            ShutterFlash.BeginAnimation(OpacityProperty, anim);
        }

        private void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            UpdateFolderUi();
            FolderListBox.ItemsSource = StorageService.Settings.ScreenshotFolders.ToList();
            FolderListBox.SelectedItem = StorageService.Settings.ActiveScreenshotFolder;
            FolderPopup.IsOpen = true;
        }

        private void CloseFolderModal_Click(object sender, RoutedEventArgs e)
        {
            FolderPopup.IsOpen = false;
        }

        private void BrowseAndSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Screenshot Destination Folder",
                    InitialDirectory = ScreenshotService.GetTargetDirectory(),
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    var selectedPath = dialog.FolderName;
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                    {
                        StorageService.Settings.CustomScreenshotPath = selectedPath;
                        var folderName = Path.GetFileName(selectedPath);
                        if (string.IsNullOrWhiteSpace(folderName)) folderName = selectedPath;

                        if (!StorageService.Settings.ScreenshotFolders.Contains(folderName))
                        {
                            StorageService.Settings.ScreenshotFolders.Insert(0, folderName);
                        }

                        StorageService.Settings.ActiveScreenshotFolder = folderName;
                        StorageService.Save();
                        UpdateFolderUi();
                        FolderPopup.IsOpen = false;
                        ShowToast($"📸 Screenshot folder set to {folderName}");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error opening folder picker: {ex.Message}");
            }
        }

        private void FolderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FolderListBox.SelectedItem is string selected)
            {
                StorageService.Settings.ActiveScreenshotFolder = selected;
                // If switching preset subfolder, clear root custom override
                if (selected == "Default" || selected == "Screenshots" || selected == "Favorites" || selected == "Recipes" || selected == "Notes" || selected == "Tutorials")
                {
                    StorageService.Settings.CustomScreenshotPath = null;
                }
                StorageService.Save();
                UpdateFolderUi();
                ShowToast($"Active folder: {selected}");
            }
        }

        private void AddNewFolder_Click(object sender, RoutedEventArgs e)
        {
            var newName = NewFolderInput.Text.Trim();
            if (!string.IsNullOrWhiteSpace(newName))
            {
                if (!StorageService.Settings.ScreenshotFolders.Contains(newName))
                {
                    StorageService.Settings.ScreenshotFolders.Add(newName);
                }
                StorageService.Settings.ActiveScreenshotFolder = newName;
                StorageService.Settings.CustomScreenshotPath = null;
                StorageService.Save();
                UpdateFolderUi();
                FolderListBox.ItemsSource = StorageService.Settings.ScreenshotFolders.ToList();
                FolderListBox.SelectedItem = newName;
                NewFolderInput.Text = "";
                ShowToast($"Created & Selected: {newName}");
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            ScreenshotService.OpenFolderInExplorer();
        }

        private void UpdateFolderUi()
        {
            var custom = StorageService.Settings.CustomScreenshotPath;
            var active = StorageService.Settings.ActiveScreenshotFolder;

            string displayText;
            if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
            {
                displayText = custom;
            }
            else
            {
                displayText = active == "Default" ? @"Pictures\Vixz" : $@"Pictures\Vixz\{active}";
            }

            if (CurrentFolderText != null) CurrentFolderText.Text = displayText;
            if (PopupCurrentFolderText != null) PopupCurrentFolderText.Text = displayText;
        }

        #endregion

        #region Sleep Timer

        private void SleepTimerBtn_Click(object sender, RoutedEventArgs e)
        {
            SleepTimerPopup.IsOpen = true;
        }

        private void CloseSleepModal_Click(object sender, RoutedEventArgs e)
        {
            SleepTimerPopup.IsOpen = false;
        }

        private void SleepSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SleepSliderValueText != null)
            {
                SleepSliderValueText.Text = $"{(int)e.NewValue} minutes";
            }
        }

        private void Preset15_Click(object sender, RoutedEventArgs e) { SleepSlider.Value = 15; }
        private void Preset30_Click(object sender, RoutedEventArgs e) { SleepSlider.Value = 30; }
        private void Preset45_Click(object sender, RoutedEventArgs e) { SleepSlider.Value = 45; }
        private void Preset60_Click(object sender, RoutedEventArgs e) { SleepSlider.Value = 60; }

        private void StartSleepTimer_Click(object sender, RoutedEventArgs e)
        {
            int minutes = (int)SleepSlider.Value;
            _lastSleepDurationMinutes = minutes;
            _sleepRemainingSeconds = minutes * 60;

            _sleepTimer?.Stop();
            _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sleepTimer.Tick += SleepTimer_Tick;
            _sleepTimer.Start();

            SleepCountdownBadge.Visibility = Visibility.Visible;
            CancelSleepBtn.Visibility = Visibility.Visible;
            SleepTimerPopup.IsOpen = false;

            ShowToast($"🌙 Sleep Timer set for {minutes}m");
        }

        private void CancelSleepTimer_Click(object sender, RoutedEventArgs e)
        {
            _sleepTimer?.Stop();
            SleepCountdownBadge.Visibility = Visibility.Collapsed;
            CancelSleepBtn.Visibility = Visibility.Collapsed;
            SleepTimerPopup.IsOpen = false;
            ShowToast("🌙 Sleep Timer Cancelled");
        }

        private void SleepTimer_Tick(object? sender, EventArgs e)
        {
            _sleepRemainingSeconds--;
            if (_sleepRemainingSeconds > 0)
            {
                var ts = TimeSpan.FromSeconds(_sleepRemainingSeconds);
                SleepCountdownText.Text = $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
            else
            {
                _sleepTimer?.Stop();
                SleepCountdownBadge.Visibility = Visibility.Collapsed;

                // Auto-pause video
                _ = VideoWebView.ExecuteScriptAsync("pauseVideo()");

                // Show 1-Click Resume Pill
                ResumeSleepText.Text = $"Resume for {_lastSleepDurationMinutes}m 🌙";
                ResumeSleepPill.Visibility = Visibility.Visible;
            }
        }

        private void ResumeSleepPill_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ResumeSleepPill.Visibility = Visibility.Collapsed;
            _ = VideoWebView.ExecuteScriptAsync("playVideo()");
            
            // Re-arm timer
            _sleepRemainingSeconds = _lastSleepDurationMinutes * 60;
            _sleepTimer?.Start();
            SleepCountdownBadge.Visibility = Visibility.Visible;

            ShowToast($"🌙 Resumed for {_lastSleepDurationMinutes}m");
        }

        #endregion

        #region Window Controls & Keyboard Shortcuts

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    MaximizeToggle();
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            _isAlwaysOnTop = !_isAlwaysOnTop;
            Topmost = _isAlwaysOnTop;
            PinButton.Foreground = _isAlwaysOnTop ? (System.Windows.Media.Brush)FindResource("AccentGold") : System.Windows.Media.Brushes.White;
            ShowToast(_isAlwaysOnTop ? "📌 Always-on-Top Pinned" : "Unpinned");
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            MaximizeToggle();
        }

        private void MaximizeToggle()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DevToolsBtn_Click(object sender, RoutedEventArgs e)
        {
            VideoWebView.CoreWebView2?.OpenDevToolsWindow();
        }

        private void FullscreenBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            _isCustomFullscreen = !_isCustomFullscreen;
            if (_isCustomFullscreen)
            {
                WindowState = WindowState.Maximized;
                SidebarCol.Width = new GridLength(0);
            }
            else
            {
                WindowState = WindowState.Normal;
                SidebarCol.Width = new GridLength(210);
            }
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (SearchBox.IsFocused) return;

            switch (e.Key)
            {
                case Key.Space:
                case Key.K:
                    await VideoWebView.ExecuteScriptAsync("togglePlay();");
                    break;
                case Key.Left:
                    await VideoWebView.ExecuteScriptAsync("seek(-5);");
                    ShowToast("⏪ -5s");
                    break;
                case Key.Right:
                    await VideoWebView.ExecuteScriptAsync("seek(5);");
                    ShowToast("⏩ +5s");
                    break;
                case Key.J:
                    await VideoWebView.ExecuteScriptAsync("seek(-10);");
                    ShowToast("⏪ -10s");
                    break;
                case Key.L:
                    await VideoWebView.ExecuteScriptAsync("seek(10);");
                    ShowToast("⏩ +10s");
                    break;
                case Key.M:
                    await VideoWebView.ExecuteScriptAsync("toggleMute();");
                    break;
                case Key.S:
                    await CaptureScreenshotAsync();
                    break;
                case Key.D:
                    await DownloadCurrentVideoAsync();
                    break;
                case Key.F:
                    ToggleFullscreen();
                    break;
                case Key.T:
                    PinButton_Click(this, new RoutedEventArgs());
                    break;
                case Key.N:
                    PlayNextVideo();
                    break;
                case Key.P:
                    PlayPreviousVideo();
                    break;
                case Key.F12:
                    VideoWebView.CoreWebView2?.OpenDevToolsWindow();
                    break;
                case Key.Escape:
                    if (SleepTimerPopup.IsOpen) SleepTimerPopup.IsOpen = false;
                    if (FolderPopup.IsOpen) FolderPopup.IsOpen = false;
                    if (_isCustomFullscreen) ToggleFullscreen();
                    break;
            }
        }

        private DispatcherTimer? _toastTimer;

        public void ShowToast(string message)
        {
            if (BottomToastText == null || BottomToastPill == null) return;

            BottomToastText.Text = message;
            BottomToastPill.Visibility = Visibility.Visible;

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _toastTimer.Tick += (s, e) =>
            {
                BottomToastPill.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
            _toastTimer.Start();
        }

        #endregion

        #region AI Copilot & Assistant

        private void AiCopilotBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleAiCopilotDrawer();
        }

        private void CloseAiCopilot_Click(object sender, RoutedEventArgs e)
        {
            AiCopilotPanel.Visibility = Visibility.Collapsed;
        }

        private void ToggleAiCopilotDrawer()
        {
            AiCopilotPanel.Visibility = AiCopilotPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (AiCopilotPanel.Visibility == Visibility.Visible)
            {
                AiPromptBox.Focus();
            }
        }

        private void AiChipSummarize_Click(object sender, RoutedEventArgs e)
        {
            _ = SubmitAiCommandAsync("Summarise this video");
        }

        private void AiChipBenny_Click(object sender, RoutedEventArgs e)
        {
            _ = SubmitAiCommandAsync("Play the latest Benny Johnson video");
        }

        private void AiChipTimer_Click(object sender, RoutedEventArgs e)
        {
            _ = SubmitAiCommandAsync("Set sleep timer for 30 minutes");
        }

        private void AiChipNews_Click(object sender, RoutedEventArgs e)
        {
            _ = SubmitAiCommandAsync("Find breaking news from today");
        }

        private async void AiPromptBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SubmitAiCommandAsync();
            }
        }

        private async void AiSendBtn_Click(object sender, RoutedEventArgs e)
        {
            await SubmitAiCommandAsync();
        }

        private async Task SubmitAiCommandAsync(string? explicitPrompt = null)
        {
            var prompt = explicitPrompt ?? AiPromptBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt)) return;

            if (AiCopilotPanel.Visibility != Visibility.Visible)
            {
                AiCopilotPanel.Visibility = Visibility.Visible;
            }

            AiPromptBox.Text = "";

            // 1. Add User Message Card
            AddUserMessageBubble(prompt);

            // 2. Add Thinking Indicator
            var thinkingCard = AddThinkingBubble();

            try
            {
                // 3. Process via AiCopilotService
                var result = await AiCopilotService.ProcessCommandAsync(prompt, _currentVideo);

                // Remove thinking indicator
                AiMessageStack.Children.Remove(thinkingCard);

                // 4. Render AI Response
                AddAiResponseBubble(result);

                // 5. Execute side effects
                if (result.Type == AiCommandType.PlayVideo && result.TargetVideo != null)
                {
                    await PlayVideoAsync(result.TargetVideo);
                    ShowToast($"▶ Playing {result.TargetVideo.Title}");
                }
                else if (result.Type == AiCommandType.ControlSeek && result.SeekSeconds.HasValue)
                {
                    await VideoWebView.ExecuteScriptAsync($"seek({result.SeekSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                    ShowToast(result.SeekSeconds.Value > 0 ? $"⏩ +{result.SeekSeconds.Value}s" : $"⏪ {result.SeekSeconds.Value}s");
                }
                else if (result.Type == AiCommandType.ControlPause)
                {
                    await VideoWebView.ExecuteScriptAsync("pauseVideo()");
                    ShowToast("⏸️ Paused");
                }
                else if (result.Type == AiCommandType.ControlPlay)
                {
                    await VideoWebView.ExecuteScriptAsync("playVideo()");
                    ShowToast("▶ Resumed");
                }
                else if (result.Type == AiCommandType.SetSleepTimer && result.TimerMinutes.HasValue)
                {
                    _lastSleepDurationMinutes = result.TimerMinutes.Value;
                    _sleepRemainingSeconds = result.TimerMinutes.Value * 60;
                    _sleepTimer?.Stop();
                    _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _sleepTimer.Tick += SleepTimer_Tick;
                    _sleepTimer.Start();
                    SleepCountdownBadge.Visibility = Visibility.Visible;
                    ShowToast($"🌙 Sleep Timer set for {result.TimerMinutes.Value}m");
                }
                else if (result.Type == AiCommandType.SearchFeed && !string.IsNullOrWhiteSpace(result.SearchQuery))
                {
                    SearchBox.Text = result.SearchQuery;
                    await PerformSearchWithFiltersAsync();
                }
            }
            catch (Exception ex)
            {
                AiMessageStack.Children.Remove(thinkingCard);
                AddSimpleAiText($"⚠️ Error: {ex.Message}");
            }

            AiChatScrollViewer.ScrollToEnd();
        }

        private void AddUserMessageBubble(string text)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E2866")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#809355FF")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12, 12, 2, 12),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(30, 4, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var tb = new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            };

            border.Child = tb;
            AiMessageStack.Children.Add(border);
        }

        private Border AddThinkingBubble()
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1C28")),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 30, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var tb = new TextBlock
            {
                Text = "✨ AI is analyzing...",
                Foreground = (System.Windows.Media.Brush)FindResource("AccentGold"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            border.Child = tb;
            AiMessageStack.Children.Add(border);
            AiChatScrollViewer.ScrollToEnd();
            return border;
        }

        private void AddSimpleAiText(string text)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#181824")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#33FFFFFF")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12, 12, 12, 2),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 20, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var tb = new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            border.Child = tb;
            AiMessageStack.Children.Add(border);
        }

        private void AddAiResponseBubble(AiCommandResult result)
        {
            var mainContainer = new StackPanel
            {
                Margin = new Thickness(0, 4, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // 1. Text Message
            if (!string.IsNullOrWhiteSpace(result.ResponseMessage))
            {
                var textBorder = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1C28")),
                    BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#33FFD700")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12, 12, 12, 2),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var tb = new TextBlock
                {
                    Text = result.ResponseMessage.Replace("**", "").Replace("*", ""),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                };
                textBorder.Child = tb;
                mainContainer.Children.Add(textBorder);
            }

            // 2. Rich Summary Card (if Summarize command)
            if (result.Summary != null)
            {
                var sum = result.Summary;
                var card = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#14141E")),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("AccentGold"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 4, 0, 6)
                };

                var cardStack = new StackPanel();

                // TL;DR Header & Block
                var tldrHeader = new TextBlock
                {
                    Text = "📌 EXECUTIVE SUMMARY",
                    Foreground = (System.Windows.Media.Brush)FindResource("AccentGold"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                cardStack.Children.Add(tldrHeader);

                var tldrBody = new TextBlock
                {
                    Text = sum.Tldr,
                    Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EEEEEE")),
                    FontSize = 12,
                    LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                cardStack.Children.Add(tldrBody);

                // Key Takeaways Header
                if (sum.KeyTakeaways.Count > 0)
                {
                    var takeHeader = new TextBlock
                    {
                        Text = "🔑 KEY TAKEAWAYS",
                        Foreground = (System.Windows.Media.Brush)FindResource("AccentGold"),
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    cardStack.Children.Add(takeHeader);

                    foreach (var point in sum.KeyTakeaways)
                    {
                        var pointRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                        var dot = new TextBlock { Text = "• ", Foreground = (System.Windows.Media.Brush)FindResource("AccentGold"), FontSize = 12 };
                        var pointText = new TextBlock { Text = point, Foreground = System.Windows.Media.Brushes.White, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 310 };
                        pointRow.Children.Add(dot);
                        pointRow.Children.Add(pointText);
                        cardStack.Children.Add(pointRow);
                    }
                }

                // Interactive Timestamp Chapters
                if (sum.Chapters.Count > 0)
                {
                    var chapHeader = new TextBlock
                    {
                        Text = "⏱️ TIMELINE CHAPTERS (CLICK TO JUMP)",
                        Foreground = (System.Windows.Media.Brush)FindResource("AccentGold"),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 6)
                    };
                    cardStack.Children.Add(chapHeader);

                    var chapWrap = new WrapPanel();
                    foreach (var chap in sum.Chapters)
                    {
                        var btn = new Button
                        {
                            Content = $"▶ {chap.TimeFormatted} {chap.Title}",
                            Style = (Style)FindResource("GlassButton"),
                            FontSize = 10.5,
                            Padding = new Thickness(6, 3, 6, 3),
                            Margin = new Thickness(0, 0, 4, 4),
                            Tag = chap.Seconds
                        };
                        btn.Click += async (s, e) =>
                        {
                            if (s is Button b && b.Tag is double sec)
                            {
                                await VideoWebView.ExecuteScriptAsync($"seek({sec.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                                ShowToast($"▶ Jumped to {chap.TimeFormatted}");
                            }
                        };
                        chapWrap.Children.Add(btn);
                    }
                    cardStack.Children.Add(chapWrap);
                }

                card.Child = cardStack;
                mainContainer.Children.Add(card);
            }

            AiMessageStack.Children.Add(mainContainer);
        }

        #endregion
    }
}
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.Media.Playback;
using Windows.Media.Core;
using System.Threading.Tasks;
using Windows.UI.Xaml.Input; 

namespace OVK_Music
{
    public sealed partial class PlaylistTracksPage : Page, IAudioPlayerPage
    {
        public static IAudioPlayerPage CurrentAudioPlayerPage { get; private set; }       
        private HttpClient httpClient = new HttpClient();
        private List<AudioItem> tracksList = new List<AudioItem>();
        public PlaylistItem AlbumItem { get; set; }
        private int currentTrackIndex = -1;
        private bool isPlaying = false;
        private bool isShuffleOn = false;
        private bool isUserDragging = false;
        private int repeatMode = 0;
        private DispatcherTimer progressTimer = new DispatcherTimer();
        private bool isCustomSliderDragging = false;
        private double sliderWidth = 0;
        private string pageId;

        public PlaylistTracksPage()
        {
            this.InitializeComponent();
            progressTimer.Interval = TimeSpan.FromMilliseconds(500);
            progressTimer.Tick += ProgressTimer_Tick;

            AudioPlayerManager.CurrentAudioPlayerPage = this;

            pageId = Guid.NewGuid().ToString();

            BackgroundMediaPlayer.Current.CurrentStateChanged += BgPlayer_CurrentStateChanged;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
    
            AudioPlayerManager.CurrentAudioPlayerPage = this;
            AudioPlayerService.Instance.SetActivePage(pageId);
            CurrentAudioPlayerPage = this;
            
            if (AudioPlayerService.Instance.GetPlaylistSource() == "playlist")
            {
                isShuffleOn = AudioPlayerService.Instance.IsShuffleEnabled();
                repeatMode = AudioPlayerService.Instance.GetRepeatMode();
                
                UpdateShuffleButtonUI();
                UpdateRepeatButtonUI();
            }

            var trackInfo = AudioPlayerService.Instance.GetCurrentTrackInfo();
            if (!string.IsNullOrEmpty(trackInfo.Title))
            {
                NowPlayingInfoTextBlock.Text = trackInfo.Artist;
                NowPlayingTextBlock.Text = trackInfo.Title;

                PlayPauseIcon.Glyph = trackInfo.IsPlaying ? "\uE769" : "\uE768";
                isPlaying = trackInfo.IsPlaying;

                if (trackInfo.Duration > 0)
                {
                    ProgressSlider.Maximum = trackInfo.Duration;
                    ProgressSlider.Value = trackInfo.Position;

                    sliderWidth = CustomSliderGrid.ActualWidth;
                    double percentage = trackInfo.Position / trackInfo.Duration;
                    UpdateCustomSliderUI(percentage);

                    CurrentTimeTextBlock.Text = TimeSpan.FromSeconds(trackInfo.Position).ToString(@"mm\:ss");
                    TotalTimeTextBlock.Text = TimeSpan.FromSeconds(trackInfo.Duration).ToString(@"mm\:ss");
                }

                if (trackInfo.IsPlaying)
                    progressTimer.Start();
            }

            if (e.Parameter is PlaylistItem)
            {
                AlbumItem = e.Parameter as PlaylistItem;
                AlbumTitleTextBlock.Text = AlbumItem.Title;
                AlbumTracksCountTextBlock.Text = $"{AlbumItem.Size} трек(ов)";
                double totalMinutes = AlbumItem.Length / 60.0;
                AlbumDurationTextBlock.Text = $"Длительность: {totalMinutes:F1} мин.";

                await LoadTracksAsync();
            }
            else
            {
                await new MessageDialog("Некорректный параметр навигации.").ShowAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            BackgroundMediaPlayer.Current.CurrentStateChanged -= BgPlayer_CurrentStateChanged;

            if (AudioPlayerManager.CurrentAudioPlayerPage == this)
            {
                AudioPlayerManager.CurrentAudioPlayerPage = null;
            }

            progressTimer.Stop();
        }

        public static string GetJsonStringValue(JsonObject obj, string key, string defaultValue = "")
        {
            if (obj.ContainsKey(key))
            {
                JsonValue value = obj.GetNamedValue(key);
                if (value.ValueType == JsonValueType.String)
                    return value.GetString();
                if (value.ValueType == JsonValueType.Number)
                    return value.ToString();
                if (value.ValueType == JsonValueType.Boolean)
                    return value.GetBoolean().ToString();
                return value.ToString();
            }
            return defaultValue;
        }

        private async Task LoadTracksAsync()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            string accessToken = localSettings.Values["AccessToken"] as string;
            object userIdObj = localSettings.Values["UserId"];
            string instance = localSettings.Values["Instance"] as string;

            if (string.IsNullOrEmpty(accessToken) || userIdObj == null || string.IsNullOrEmpty(instance))
            {
                await new MessageDialog("Ошибка: отсутствуют данные авторизации.").ShowAsync();
                return;
            }

            int userId = Convert.ToInt32(userIdObj);
            string requestUrl = $"https://{instance}/method/audio.get?owner_id={userId}&album_id={AlbumItem.Id}&access_token={accessToken}&offset=0";

            try
            {
                var response = await httpClient.GetAsync(requestUrl);
                string jsonResponse = await response.Content.ReadAsStringAsync();
                JsonObject rootObject = JsonObject.Parse(jsonResponse);

                if (rootObject.ContainsKey("error"))
                {
                    JsonObject errorObj = rootObject.GetNamedObject("error");
                    string errorMsg = errorObj.GetNamedString("error_msg", "Неизвестная ошибка");
                    await new MessageDialog("Ошибка API: " + errorMsg).ShowAsync();
                    return;
                }
                if (!rootObject.ContainsKey("response"))
                {
                    await new MessageDialog("Неверный формат ответа API.").ShowAsync();
                    return;
                }

                JsonObject responseObject = rootObject.GetNamedObject("response");
                if (!responseObject.ContainsKey("items"))
                {
                    await new MessageDialog("В ответе API отсутствует список треков.").ShowAsync();
                    return;
                }

                JsonArray itemsArray = responseObject.GetNamedArray("items");
                tracksList.Clear();
                foreach (var item in itemsArray)
                {
                    if (item.ValueType == JsonValueType.Object)
                    {
                        JsonObject trackObj = item.GetObject();
                        var track = new AudioItem
                        {
                            UniqueId = GetJsonStringValue(trackObj, "unique_id", ""),
                            Id = (int)trackObj.GetNamedNumber("id", 0),
                            Artist = GetJsonStringValue(trackObj, "artist", ""),
                            Title = GetJsonStringValue(trackObj, "title", ""),
                            Duration = (int)trackObj.GetNamedNumber("duration", 0),
                            Url = GetJsonStringValue(trackObj, "url", "")
                        };
                        tracksList.Add(track);
                    }
                }
                TracksListView.ItemsSource = tracksList;
            }
            catch (Exception ex)
            {
                await new MessageDialog("Ошибка при загрузке треков:\n" + ex.Message).ShowAsync();
            }
        }

        private void TracksListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            AudioItem clickedTrack = e.ClickedItem as AudioItem;
            if (clickedTrack != null)
            {
                currentTrackIndex = tracksList.IndexOf(clickedTrack);
                StartPlayback(clickedTrack);
            }
        }

        private async void StartPlayback(AudioItem track)
        {
            try
            {
                AudioPlayerService.Instance.ForceChangePlaylistSource("playlist");
                
                currentTrackIndex = tracksList.IndexOf(track);
                
                AudioPlayerService.Instance.SetCurrentPlaylist(
                    tracksList, currentTrackIndex, "playlist", isShuffleOn, repeatMode);
                
                AudioPlayerService.Instance.SetActivePage(pageId);
                AudioPlayerManager.CurrentAudioPlayerPage = this;
                
                AudioPlayerService.Instance.SetCurrentTrack(track.Title, track.Artist);
                NowPlayingInfoTextBlock.Text = $"{track.Artist}";
                NowPlayingTextBlock.Text = $"{track.Title}";
                PlayPauseIcon.Glyph = "\uE769";
                progressTimer.Start();

                var bgPlayer = BackgroundMediaPlayer.Current;
                bgPlayer.Source = MediaSource.CreateFromUri(new Uri(track.Url));
                bgPlayer.Play();
                isPlaying = true;

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    var smtc = bgPlayer.SystemMediaTransportControls;
                    smtc.IsEnabled = true;
                    smtc.IsPlayEnabled = true;
                    smtc.IsPauseEnabled = true;
                    smtc.IsNextEnabled = true;
                    smtc.IsPreviousEnabled = true;
                    smtc.DisplayUpdater.Type = Windows.Media.MediaPlaybackType.Music;
                    smtc.DisplayUpdater.MusicProperties.Title = track.Title;
                    smtc.DisplayUpdater.MusicProperties.Artist = track.Artist;
                    smtc.DisplayUpdater.Update();
                });

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    TracksListView.SelectedItem = track;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка воспроизведения: " + ex);
            }
        }

        public void HandleMediaEnded()
        {
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (repeatMode == 2 && currentTrackIndex >= 0)
                {
                    StartPlayback(tracksList[currentTrackIndex]);
                    return;
                }

                if (repeatMode == 1 || isShuffleOn)
                {
                    NextButton_Click(null, null);
                    return;
                }

                if (currentTrackIndex < tracksList.Count - 1)
                {
                    currentTrackIndex++;
                    StartPlayback(tracksList[currentTrackIndex]);
                }
                else
                {
                    isPlaying = false;
                    PlayPauseIcon.Glyph = "\uE768";
                    progressTimer.Stop();

                    var bgPlayer = BackgroundMediaPlayer.Current;
                    bgPlayer.Pause();
                    bgPlayer.Source = null;

                    NowPlayingTextBlock.Text = "";
                    NowPlayingInfoTextBlock.Text = "Ничего не воспроизводится";
                    CurrentTimeTextBlock.Text = "00:00";
                    TotalTimeTextBlock.Text = "00:00";
                    UpdateCustomSliderUI(0);
                }
            });
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            var bgPlayer = BackgroundMediaPlayer.Current;
            if (isPlaying)
            {
                bgPlayer.Pause();
                isPlaying = false;
                PlayPauseIcon.Glyph = "\uE768";
            }
            else
            {
                bgPlayer.Play();
                isPlaying = true;
                PlayPauseIcon.Glyph = "\uE769";
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayerService.Instance.NextTrack();
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayerService.Instance.PreviousTrack();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            isShuffleOn = !isShuffleOn;
            
            AudioPlayerService.Instance.UpdatePlaybackSettings(isShuffleOn, repeatMode);
            
            var icon = ((sender as Button).Content as FontIcon);
            if (icon != null)
            {
                icon.Foreground = isShuffleOn 
                    ? (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"] 
                    : (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"];
            }
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            repeatMode = (repeatMode + 1) % 3;
            
            AudioPlayerService.Instance.UpdatePlaybackSettings(isShuffleOn, repeatMode);
            
            if (repeatMode == 0)
            {
                RepeatIcon.Glyph = "\uE8EE";
                RepeatIcon.Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"];
            }
            else if (repeatMode == 1)
            {
                RepeatIcon.Glyph = "\uE8EE";
                RepeatIcon.Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
            }
            else if (repeatMode == 2)
            {
                RepeatIcon.Glyph = "\uE8ED";
                RepeatIcon.Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
            }
        }

        private void CustomSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            isCustomSliderDragging = true;
            isUserDragging = true;
            progressTimer.Stop();
            
            var point = e.GetCurrentPoint(CustomSliderGrid);
            sliderWidth = CustomSliderGrid.ActualWidth;
            
            double percentage = point.Position.X / sliderWidth;
            double newValue = percentage * ProgressSlider.Maximum;
            
            ProgressSlider.Value = newValue;
            
            UpdateCustomSliderUI(percentage);
            
            TimeSpan newPosition = TimeSpan.FromSeconds(newValue);
            CurrentTimeTextBlock.Text = newPosition.ToString(@"mm\:ss");
            
            CustomSliderGrid.CapturePointer(e.Pointer);
        }

        private void CustomSlider_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!isCustomSliderDragging) return;
            
            var point = e.GetCurrentPoint(CustomSliderGrid);
            
            double x = Math.Max(0, Math.Min(point.Position.X, sliderWidth));
            
            double percentage = x / sliderWidth;
            double newValue = percentage * ProgressSlider.Maximum;
            
            ProgressSlider.Value = newValue;
            
            UpdateCustomSliderUI(percentage);
            
            TimeSpan newPosition = TimeSpan.FromSeconds(newValue);
            CurrentTimeTextBlock.Text = newPosition.ToString(@"mm\:ss");
        }

        private void CustomSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!isCustomSliderDragging) return;
            
            CustomSliderGrid.ReleasePointerCapture(e.Pointer);
            
            isCustomSliderDragging = false;
            
            double newSeconds = ProgressSlider.Value;
            MoveToSeconds(newSeconds);
        }
        
        private void UpdateCustomSliderUI(double percentage)
        {
            percentage = Math.Max(0, Math.Min(percentage, 1));
            
            double newWidth = percentage * sliderWidth;
            ProgressRect.Width = newWidth;
            
            SliderThumb.Margin = new Thickness(newWidth - 8, 0, 0, 0);
        }
        
        private async void MoveToSeconds(double seconds)
        {
            progressTimer.Stop();
            
            var bgPlayer = BackgroundMediaPlayer.Current;
            var playbackSession = bgPlayer.PlaybackSession;
            TimeSpan newPosition = TimeSpan.FromSeconds(seconds);
            
            System.Diagnostics.Debug.WriteLine($"Перемотка на {newPosition}");
            
            bool wasPlaying = (playbackSession.PlaybackState == MediaPlaybackState.Playing);
            bgPlayer.Pause();
            
            try {
                await Task.Delay(100);
                bgPlayer.Position = newPosition;
                await Task.Delay(200);
                
                if (wasPlaying)
                    bgPlayer.Play();
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Ошибка перемотки: {ex.Message}");
            }
            
            isUserDragging = false;
            await Task.Delay(100);
            progressTimer.Start();
        }

        private void ProgressTimer_Tick(object sender, object e)
        {
            var bgPlayer = BackgroundMediaPlayer.Current;
            var playbackSession = bgPlayer.PlaybackSession;
            
            if (playbackSession.NaturalDuration != TimeSpan.Zero && !isUserDragging)
            {
                ProgressSlider.Maximum = playbackSession.NaturalDuration.TotalSeconds;
                ProgressSlider.Value = playbackSession.Position.TotalSeconds;
                
                double percentage = playbackSession.Position.TotalSeconds / 
                                  playbackSession.NaturalDuration.TotalSeconds;
                sliderWidth = CustomSliderGrid.ActualWidth;
                UpdateCustomSliderUI(percentage);
            }
            
            CurrentTimeTextBlock.Text = playbackSession.Position.ToString(@"mm\:ss");
            TotalTimeTextBlock.Text = playbackSession.NaturalDuration.ToString(@"mm\:ss");
        }

        public void UpdatePlaybackStatus(bool playing, string trackInfo)
        {
            isPlaying = playing;
            NowPlayingTextBlock.Text = trackInfo;
            PlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";
            
            if (playing)
                progressTimer.Start();
            else
                progressTimer.Stop();
        }

        public void NextTrack()
        {
            NextButton_Click(null, null);
        }

        public void PreviousTrack()
        {
            PrevButton_Click(null, null);
        }

        private void BgPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                var bgPlayer = BackgroundMediaPlayer.Current;

                if (bgPlayer.CurrentState == MediaPlayerState.Playing)
                {
                    isPlaying = true;
                    PlayPauseIcon.Glyph = "\uE769";
                    progressTimer.Start();
                }
                else if (bgPlayer.CurrentState == MediaPlayerState.Paused)
                {
                    isPlaying = false;
                    PlayPauseIcon.Glyph = "\uE768";
                    progressTimer.Stop();
                }
            });
        }

        public void UpdateActiveTrack(int index, string source)
        {
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (source == "playlist")
                {
                    currentTrackIndex = index;
                    if (index >= 0 && index < tracksList.Count)
                    {
                        TracksListView.SelectedItem = tracksList[currentTrackIndex];
                        NowPlayingTextBlock.Text = $"{tracksList[currentTrackIndex].Title}";
                        NowPlayingInfoTextBlock.Text = $"{tracksList[currentTrackIndex].Artist}";
                        PlayPauseIcon.Glyph = "\uE769";
                        isPlaying = true;
                        progressTimer.Start();
                    }
                }
            });
        }

        public void PlaybackStopped()
        {
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                isPlaying = false;
                PlayPauseIcon.Glyph = "\uE768";
                progressTimer.Stop();

                NowPlayingTextBlock.Text = "Ничего не воспроизводится";
                CurrentTimeTextBlock.Text = "00:00";
                TotalTimeTextBlock.Text = "00:00";
                UpdateCustomSliderUI(0);
            });
        }

        private void UpdateShuffleButtonUI()
        {
            var icon = ShuffleButton.Content as FontIcon;
            if (icon != null)
            {
                icon.Foreground = isShuffleOn 
                    ? (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"] 
                    : (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"];
            }
        }

        private void UpdateRepeatButtonUI()
        {
            if (repeatMode == 0)
            {
                RepeatIcon.Glyph = "\uE8EE";
                RepeatIcon.Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"];
            }
            else if (repeatMode == 1)
            {
                RepeatIcon.Glyph = "\uE8EE";
                RepeatIcon.Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
            }
            else if (repeatMode == 2)
            {
                RepeatIcon.Glyph = "\uE8ED";
                RepeatIcon.Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
            }
        }
    }
}

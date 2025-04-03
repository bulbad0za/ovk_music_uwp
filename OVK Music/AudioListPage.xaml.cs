using System;
using System.Collections.Generic;
using System.Net.Http;
using Windows.Data.Json;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media;
using System.Threading.Tasks;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Controls.Primitives;




namespace OVK_Music
{
    public sealed partial class AudioListPage : Page, IAudioPlayerPage
    {
        private string pageId;
        public static IAudioPlayerPage CurrentAudioPlayerPage { get; private set; }
        private HttpClient httpClient = new HttpClient();
        private List<AudioItem> audioList = new List<AudioItem>();
        private int currentTrackIndex = -1;
        private bool isPlaying = false;
        private bool isShuffleOn = false;
        /// <summary>
        /// Режимы повтора треков:
        /// 0 – повтор выключен,
        /// 1 – повтор всех,
        /// 2 – повтор одного
        /// </summary>
        private int repeatMode = 0;

        private DispatcherTimer progressTimer = new DispatcherTimer();

        private bool isUserDragging = false;

        private bool isCustomSliderDragging = false;
        private double sliderWidth = 0;

        public AudioListPage()
        {
            this.InitializeComponent();
            progressTimer.Interval = TimeSpan.FromMilliseconds(500);
            progressTimer.Tick += ProgressTimer_Tick;

            AudioPlayerManager.CurrentAudioPlayerPage = this;

            pageId = Guid.NewGuid().ToString();

            BackgroundMediaPlayer.Current.CurrentStateChanged += BgPlayer_CurrentStateChanged;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AudioPlayerManager.CurrentAudioPlayerPage = this;
            AudioPlayerService.Instance.SetActivePage(pageId);
            CurrentAudioPlayerPage = this;
            LoadAudioAsync();

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

        private async void LoadAudioAsync()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            string accessToken = localSettings.Values["AccessToken"] as string;
            object userIdObj = localSettings.Values["UserId"];
            string instance = localSettings.Values["Instance"] as string;

            if (string.IsNullOrEmpty(accessToken) || userIdObj == null || string.IsNullOrEmpty(instance))
            {
                Frame rootFrame = Window.Current.Content as Frame;
                rootFrame.Navigate(typeof(MainPage));
                return;
            }

            int userId = Convert.ToInt32(userIdObj);
            string requestUrl = $"https://{instance}/method/audio.get?owner_id={userId}&access_token={accessToken}&offset=0";

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
                    await new MessageDialog("Неверный формат ответа API.").ShowAsync();
                    return;
                }

                JsonArray itemsArray = responseObject.GetNamedArray("items");
                audioList.Clear();
                foreach (var item in itemsArray)
                {
                    if (item.ValueType == JsonValueType.Object)
                    {
                        JsonObject audioObj = item.GetObject();
                        var audio = new AudioItem
                        {
                            UniqueId = GetJsonStringValue(audioObj, "unique_id", ""),
                            Id = (int)audioObj.GetNamedNumber("id", 0),
                            Artist = GetJsonStringValue(audioObj, "artist", ""),
                            Title = GetJsonStringValue(audioObj, "title", ""),
                            Duration = (int)audioObj.GetNamedNumber("duration", 0),
                            Url = GetJsonStringValue(audioObj, "url", "")
                        };
                        audioList.Add(audio);
                    }
                }
                AudioListView.ItemsSource = audioList;
            }
            catch (Exception ex)
            {
                await new MessageDialog("Ошибка при загрузке аудио:\n" + ex.Message).ShowAsync();
            }
        }

        private void AudioListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            AudioItem clickedAudio = e.ClickedItem as AudioItem;
            if (clickedAudio != null)
            {
                currentTrackIndex = audioList.IndexOf(clickedAudio);
                StartPlayback(clickedAudio);
            }
        }

        private async void StartPlayback(AudioItem track)
        {
            try
                {
                    var currentSource = AudioPlayerService.ActivePlaylistSource;
                    if (!string.IsNullOrEmpty(currentSource) && currentSource != "main")
                    {
                        var player = BackgroundMediaPlayer.Current;
                        player.Pause();
                        player.Source = null;
                        await Task.Delay(100);
                    }

                    AudioPlayerService.Instance.ForceChangePlaylistSource("main");
                    
                    currentTrackIndex = audioList.IndexOf(track);

                    AudioPlayerService.Instance.SetCurrentPlaylist(
                        audioList, currentTrackIndex, "main", isShuffleOn, repeatMode);

                    AudioPlayerService.Instance.SetActivePage(pageId);
                    AudioPlayerManager.CurrentAudioPlayerPage = this;

                    NowPlayingTextBlock.Text = track.Title;
                    NowPlayingInfoTextBlock.Text = track.Artist;
                    PlayPauseIcon.Glyph = "\uE769";
                    progressTimer.Start();

                    AudioPlayerService.Instance.SetCurrentTrack(track.Title, track.Artist);
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
                    AudioListView.SelectedItem = track;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка воспроизведения: " + ex);
            }
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

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioList.Count == 0) return;
            
            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(audioList.Count);
                StartPlayback(audioList[currentTrackIndex]);
                return;
            }
            
            if (repeatMode > 0 || currentTrackIndex > 0)
            {
                currentTrackIndex = (currentTrackIndex - 1 + audioList.Count) % audioList.Count;
                StartPlayback(audioList[currentTrackIndex]);
            }
            else
            {
                if (currentTrackIndex == 0)
                {
                    var bgPlayer = BackgroundMediaPlayer.Current;
                    bgPlayer.Position = TimeSpan.Zero;
                    bgPlayer.Play();
                    isPlaying = true;
                }
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioList.Count == 0) return;
            
            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(audioList.Count);
                StartPlayback(audioList[currentTrackIndex]);
                return;
            }
            
            if (repeatMode > 0 || currentTrackIndex < audioList.Count - 1)
            {
                currentTrackIndex = (currentTrackIndex + 1) % audioList.Count;
                StartPlayback(audioList[currentTrackIndex]);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Достигнут конец плейлиста без режима повтора");
            }
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

        public void HandleMediaEnded()
        {
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (repeatMode == 2 && currentTrackIndex >= 0)
                {
                    StartPlayback(audioList[currentTrackIndex]);
                    return;
                }

                if (repeatMode == 1 || isShuffleOn)
                {
                    NextButton_Click(null, null);
                    return;
                }

                if (currentTrackIndex < audioList.Count - 1)
                {
                    currentTrackIndex++;
                    StartPlayback(audioList[currentTrackIndex]);
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

        public void NextTrack()
        {
            if (audioList.Count == 0) return;
            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(audioList.Count);
            }
            else
            {
                currentTrackIndex = (currentTrackIndex + 1) % audioList.Count;
            }
            StartPlayback(audioList[currentTrackIndex]);
        }

        public void PreviousTrack()
        {
            if (audioList.Count == 0) return;
            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(audioList.Count);
            }
            else
            {
                currentTrackIndex = (currentTrackIndex - 1 + audioList.Count) % audioList.Count;
            }
            StartPlayback(audioList[currentTrackIndex]);
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            isShuffleOn = !isShuffleOn;
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

        private void AudioPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            var bgPlayer = BackgroundMediaPlayer.Current;
            var playbackSession = bgPlayer.PlaybackSession;
            if (playbackSession.NaturalDuration != TimeSpan.Zero)
            {
                ProgressSlider.Maximum = playbackSession.NaturalDuration.TotalSeconds;
                ProgressSlider.Value = playbackSession.Position.TotalSeconds;
            }
            if (isPlaying)
            {
                bgPlayer.Play();
            }
        }

        private void AudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (repeatMode == 2)
            {
                StartPlayback(audioList[currentTrackIndex]);
            }
            else if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(audioList.Count);
                StartPlayback(audioList[currentTrackIndex]);
            }
            else if (repeatMode == 1)
            {
                currentTrackIndex = (currentTrackIndex + 1) % audioList.Count;
                StartPlayback(audioList[currentTrackIndex]);
            }
            else
            {
                if (currentTrackIndex < audioList.Count - 1)
                {
                    currentTrackIndex++;
                    StartPlayback(audioList[currentTrackIndex]);
                }
                else
                {
                    AudioPlayer.Stop();
                    isPlaying = false;
                    PlayPauseIcon.Glyph = "\uE768";
                }
            }
        }

        private void AudioPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            progressTimer.Stop();
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
                if (source == "main")
                {
                    currentTrackIndex = index;
                    if (index >= 0 && index < audioList.Count)
                    {
                        AudioListView.SelectedItem = audioList[currentTrackIndex];
                        NowPlayingTextBlock.Text = audioList[currentTrackIndex].Title;
                        NowPlayingInfoTextBlock.Text = audioList[currentTrackIndex].Artist;
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

                if (this is AudioListPage)
                {
                    NowPlayingTextBlock.Text = "";
                    NowPlayingInfoTextBlock.Text = "Ничего не воспроизводится";
                }
                else
                {
                    NowPlayingTextBlock.Text = "Ничего не воспроизводится";
                }

                CurrentTimeTextBlock.Text = "00:00";
                TotalTimeTextBlock.Text = "00:00";
                UpdateCustomSliderUI(0);
            });
        }
    }
}
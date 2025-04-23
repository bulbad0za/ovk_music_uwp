using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Net.Http;
using Windows.UI.Popups;
using Windows.Media.Playback;
using Windows.Media.Core;
using Windows.Media;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Input;
using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;

namespace OVK_Music
{
    public sealed partial class SearchPage : Page, IAudioPlayerPage
    {
        private string pageId;
        private HttpClient httpClient = new HttpClient();
        private List<AudioItem> searchResults = new List<AudioItem>();
        private List<AudioItem> recentSearches = new List<AudioItem>();
        private int currentTrackIndex = -1;
        private bool isPlaying = false;
        private bool isShuffleOn = false;
        private int repeatMode = 0;

        private DispatcherTimer progressTimer = new DispatcherTimer();
        private bool isUserDragging = false;
        private bool isCustomSliderDragging = false;
        private double sliderWidth = 0;

        private AudioItem selectedTrackForMenu;

        public SearchPage()
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

            DataTransferManager dataTransferManager = DataTransferManager.GetForCurrentView();
            dataTransferManager.DataRequested -= DataTransferManager_DataRequested;

            BackgroundMediaPlayer.Current.CurrentStateChanged -= BgPlayer_CurrentStateChanged;

            if (AudioPlayerManager.CurrentAudioPlayerPage == this)
            {
                AudioPlayerManager.CurrentAudioPlayerPage = null;
            }

            progressTimer.Stop();
        }

        private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                string query = args.QueryText;
                if (!string.IsNullOrEmpty(query))
                {
                    StatusTextBlock.Text = "Поиск...";
                    LoadingRing.IsActive = true;
                    LoadingRing.Visibility = Visibility.Visible;
                    SearchResultsListView.Visibility = Visibility.Collapsed;

                    await SearchTracksAsync(query);

                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                    SearchResultsListView.Visibility = Visibility.Visible;

                    if (searchResults.Count > 0)
                        StatusTextBlock.Text = $"Найдено результатов: {searchResults.Count}";
                    else
                        StatusTextBlock.Text = "Ничего не найдено";
                }
            }
            catch (Exception ex)
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = "Произошла ошибка при поиске";
                System.Diagnostics.Debug.WriteLine($"Ошибка в SearchBox_QuerySubmitted: {ex.Message}");
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                // Если пользователь ввел текст (а не выбрал из автозавершения)
                if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    string query = sender.Text;

                    // Если строка поиска пуста, очищаем результаты
                    if (string.IsNullOrEmpty(query))
                    {
                        searchResults.Clear();
                        SearchResultsListView.ItemsSource = null;
                        StatusTextBlock.Text = "Введите запрос для поиска";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в SearchBox_TextChanged: {ex.Message}");
            }
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            // Обработка выбора из подсказок (при необходимости)
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchBox.Text = string.Empty;
                searchResults.Clear();
                SearchResultsListView.ItemsSource = null;
                StatusTextBlock.Text = "Введите запрос для поиска";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в ClearButton_Click: {ex.Message}");
            }
        }

        private async Task SearchTracksAsync(string query)
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                string accessToken = localSettings.Values["AccessToken"] as string;
                object userIdObj = localSettings.Values["UserId"];
                string instance = localSettings.Values["Instance"] as string;

                if (string.IsNullOrEmpty(accessToken) || userIdObj == null || string.IsNullOrEmpty(instance))
                {
                    await new MessageDialog("Ошибка авторизации. Пожалуйста, войдите снова.").ShowAsync();
                    Frame rootFrame = Window.Current.Content as Frame;
                    rootFrame.Navigate(typeof(MainPage));
                    return;
                }

                int userId = Convert.ToInt32(userIdObj);

                // Исправленный URL - используем параметр q вместо query и увеличиваем максимум до 300
                string requestUrl = $"https://{instance}/method/audio.search?q={Uri.EscapeDataString(query)}&access_token={accessToken}&count=300";
                System.Diagnostics.Debug.WriteLine($"Запрос поиска: {requestUrl}");

                var response = await httpClient.GetAsync(requestUrl);
                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Для отладки выводим ответ сервера
                System.Diagnostics.Debug.WriteLine("API Response: " + jsonResponse);

                if (string.IsNullOrEmpty(jsonResponse))
                {
                    System.Diagnostics.Debug.WriteLine("Пустой ответ от API");
                    await new MessageDialog("Сервер вернул пустой ответ. Пожалуйста, попробуйте позже.").ShowAsync();
                    return;
                }

                JsonObject rootObject;
                try
                {
                    rootObject = JsonObject.Parse(jsonResponse);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка парсинга JSON: {ex.Message}");
                    await new MessageDialog("Ошибка при обработке ответа API.").ShowAsync();
                    return;
                }

                if (rootObject.ContainsKey("error"))
                {
                    try
                    {
                        JsonObject errorObj = rootObject.GetNamedObject("error");
                        string errorMsg = "Неизвестная ошибка";

                        if (errorObj.ContainsKey("error_msg") && errorObj.GetNamedValue("error_msg").ValueType == JsonValueType.String)
                        {
                            errorMsg = errorObj.GetNamedString("error_msg", "Неизвестная ошибка");
                        }
                        else if (errorObj.ContainsKey("error_code"))
                        {
                            int errorCode = (int)errorObj.GetNamedNumber("error_code", 0);
                            errorMsg = $"Код ошибки: {errorCode}";
                        }

                        System.Diagnostics.Debug.WriteLine($"API вернул ошибку: {errorMsg}");
                        await new MessageDialog("Ошибка API: " + errorMsg).ShowAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ошибка при обработке ошибки API: {ex.Message}");
                        await new MessageDialog("Ошибка при выполнении запроса к API.").ShowAsync();
                    }
                    return;
                }

                if (!rootObject.ContainsKey("response"))
                {
                    System.Diagnostics.Debug.WriteLine("API вернул ответ без поля response");
                    await new MessageDialog("Неверный формат ответа API (отсутствует поле response).").ShowAsync();
                    return;
                }

                try
                {
                    JsonObject responseObject = rootObject.GetNamedObject("response");

                    // Очищаем предыдущие результаты
                    searchResults.Clear();

                    // Пробуем обработать различные форматы ответа
                    bool resultsProcessed = false;

                    // Формат с items
                    if (responseObject.ContainsKey("items") &&
                        responseObject.GetNamedValue("items").ValueType == JsonValueType.Array)
                    {
                        System.Diagnostics.Debug.WriteLine("Обрабатываем формат ответа с items");
                        JsonArray itemsArray = responseObject.GetNamedArray("items");
                        ParseAudioItems(itemsArray);
                        resultsProcessed = true;
                    }
                    // Формат с audio и count
                    else if (responseObject.ContainsKey("count") &&
                             responseObject.ContainsKey("audio") &&
                             responseObject.GetNamedValue("audio").ValueType == JsonValueType.Array)
                    {
                        System.Diagnostics.Debug.WriteLine("Обрабатываем формат ответа с audio");
                        JsonArray audioArray = responseObject.GetNamedArray("audio");
                        ParseAudioItems(audioArray);
                        resultsProcessed = true;
                    }
                    // Массив непосредственно в response
                    else if (responseObject.ValueType == JsonValueType.Array)
                    {
                        System.Diagnostics.Debug.WriteLine("Обрабатываем формат ответа, где response - массив");
                        JsonArray directArray = JsonValue.Parse(responseObject.ToString()).GetArray();
                        ParseAudioItems(directArray);
                        resultsProcessed = true;
                    }
                    else
                    {
                        // Перебор всех ключей в поиске массива треков
                        System.Diagnostics.Debug.WriteLine("Перебираем все ключи в response");
                        foreach (var key in responseObject.Keys)
                        {
                            System.Diagnostics.Debug.WriteLine($"Проверяем ключ: {key}");
                            try
                            {
                                if (responseObject.GetNamedValue(key).ValueType == JsonValueType.Array)
                                {
                                    var arr = responseObject.GetNamedArray(key);
                                    if (arr.Count > 0)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Найден массив в ключе {key}, элементов: {arr.Count}");
                                        if (arr[0].ValueType == JsonValueType.Object)
                                        {
                                            ParseAudioItems(arr);
                                            resultsProcessed = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Ошибка при обработке ключа {key}: {ex.Message}");
                            }
                        }
                    }

                    if (!resultsProcessed)
                    {
                        // Если ничего не нашли, выводим структуру ответа для анализа
                        System.Diagnostics.Debug.WriteLine("Не удалось найти аудиозаписи в ответе. Структура ответа:");
                        foreach (var key in responseObject.Keys)
                        {
                            System.Diagnostics.Debug.WriteLine($"- Ключ: {key}, Тип: {responseObject.GetNamedValue(key).ValueType}");
                        }
                        await new MessageDialog("Поиск не дал результатов или формат ответа API не поддерживается.").ShowAsync();
                    }

                    System.Diagnostics.Debug.WriteLine($"Найдено аудиозаписей: {searchResults.Count}");
                    SearchResultsListView.ItemsSource = searchResults;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при обработке ответа API: {ex.Message}");
                    await new MessageDialog("Ошибка при обработке результатов поиска.").ShowAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка в SearchTracksAsync: {ex.Message}");
                await new MessageDialog("Ошибка при поиске аудио:\n" + ex.Message).ShowAsync();
            }
        }

        // Метод для парсинга массива аудио-объектов
        private void ParseAudioItems(JsonArray itemsArray)
        {
            System.Diagnostics.Debug.WriteLine($"Парсинг массива аудио-объектов, элементов: {itemsArray.Count}");

            for (int i = 0; i < itemsArray.Count; i++)
            {
                try
                {
                    var item = itemsArray[i];
                    if (item.ValueType == JsonValueType.Object)
                    {
                        JsonObject audioObj = item.GetObject();

                        // Логируем ключи объекта для первого и последнего элемента
                        if (i == 0 || i == itemsArray.Count - 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"Ключи аудио объекта #{i}:");
                            foreach (var key in audioObj.Keys)
                            {
                                System.Diagnostics.Debug.WriteLine($"- {key}: {audioObj.GetNamedValue(key).ValueType}");
                            }
                        }

                        var audio = new AudioItem
                        {
                            UniqueId = GetJsonStringValue(audioObj, "unique_id", ""),
                            Id = (int)audioObj.GetNamedNumber("id", 0),
                            OwnerId = (int)audioObj.GetNamedNumber("owner_id", 0), // Добавляем получение owner_id
                            Artist = GetJsonStringValue(audioObj, "artist", ""),
                            Title = GetJsonStringValue(audioObj, "title", ""),
                            Duration = (int)audioObj.GetNamedNumber("duration", 0),
                            Url = GetJsonStringValue(audioObj, "url", "")
                        };

                        // Если owner_id не был найден, и есть unique_id, пробуем извлечь из него
                        if (audio.OwnerId == 0 && !string.IsNullOrEmpty(audio.UniqueId))
                        {
                            string[] parts = audio.UniqueId.Split('_');
                            if (parts.Length >= 2)
                            {
                                int extractedOwnerId;
                                if (int.TryParse(parts[0], out extractedOwnerId))
                                {
                                    audio.OwnerId = extractedOwnerId;
                                }
                            }
                        }

                        // Вывод отладочной информации для first-last объектов
                        if (i == 0 || i == itemsArray.Count - 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"Аудио #{i}: Id={audio.Id}, OwnerId={audio.OwnerId}, UniqueId={audio.UniqueId}");
                        }

                        // Проверка на валидность объекта
                        if (!string.IsNullOrEmpty(audio.Url) && !string.IsNullOrEmpty(audio.Title))
                        {
                            searchResults.Add(audio);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Пропущен невалидный объект аудио #{i} - отсутствует URL или название");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Элемент #{i} не является объектом, тип: {item.ValueType}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при парсинге элемента #{i}: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"Успешно обработано аудио объектов: {searchResults.Count}");
        }

        // Безопасное получение строкового значения из JSON
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

        private void SearchResultsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            AudioItem clickedAudio = e.ClickedItem as AudioItem;
            if (clickedAudio != null)
            {
                currentTrackIndex = searchResults.IndexOf(clickedAudio);
                StartPlayback(clickedAudio);
            }
        }

        private async void StartPlayback(AudioItem track)
        {
            try
            {
                var currentSource = AudioPlayerService.ActivePlaylistSource;
                if (!string.IsNullOrEmpty(currentSource) && currentSource != "search")
                {
                    var player = BackgroundMediaPlayer.Current;
                    player.Pause();
                    player.Source = null;
                    await Task.Delay(100);
                }

                AudioPlayerService.Instance.ForceChangePlaylistSource("search");

                currentTrackIndex = searchResults.IndexOf(track);

                AudioPlayerService.Instance.SetCurrentPlaylist(
                    searchResults, currentTrackIndex, "search", isShuffleOn, repeatMode);

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
                    smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
                    smtc.DisplayUpdater.MusicProperties.Title = track.Title;
                    smtc.DisplayUpdater.MusicProperties.Artist = track.Artist;
                    smtc.DisplayUpdater.Update();
                });

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    SearchResultsListView.SelectedItem = track;
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
            if (searchResults.Count == 0) return;

            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(searchResults.Count);
                StartPlayback(searchResults[currentTrackIndex]);
                return;
            }

            if (repeatMode > 0 || currentTrackIndex > 0)
            {
                currentTrackIndex = (currentTrackIndex - 1 + searchResults.Count) % searchResults.Count;
                StartPlayback(searchResults[currentTrackIndex]);
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
            if (searchResults.Count == 0) return;

            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(searchResults.Count);
                StartPlayback(searchResults[currentTrackIndex]);
                return;
            }

            if (repeatMode > 0 || currentTrackIndex < searchResults.Count - 1)
            {
                currentTrackIndex = (currentTrackIndex + 1) % searchResults.Count;
                StartPlayback(searchResults[currentTrackIndex]);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Достигнут конец плейлиста без режима повтора");
            }
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

            try
            {
                await Task.Delay(100);
                bgPlayer.Position = newPosition;
                await Task.Delay(200);

                if (wasPlaying)
                    bgPlayer.Play();
            }
            catch (Exception ex)
            {
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
                if (source == "search")
                {
                    currentTrackIndex = index;
                    if (index >= 0 && index < searchResults.Count)
                    {
                        SearchResultsListView.SelectedItem = searchResults[currentTrackIndex];
                        NowPlayingTextBlock.Text = searchResults[currentTrackIndex].Title;
                        NowPlayingInfoTextBlock.Text = searchResults[currentTrackIndex].Artist;
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

                NowPlayingTextBlock.Text = "";
                NowPlayingInfoTextBlock.Text = "Ничего не воспроизводится";
                CurrentTimeTextBlock.Text = "00:00";
                TotalTimeTextBlock.Text = "00:00";
                UpdateCustomSliderUI(0);
            });
        }

        public void NextTrack()
        {
            if (searchResults.Count == 0) return;
            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(searchResults.Count);
            }
            else
            {
                currentTrackIndex = (currentTrackIndex + 1) % searchResults.Count;
            }
            StartPlayback(searchResults[currentTrackIndex]);
        }

        public void PreviousTrack()
        {
            if (searchResults.Count == 0) return;
            if (isShuffleOn)
            {
                Random rnd = new Random();
                currentTrackIndex = rnd.Next(searchResults.Count);
            }
            else
            {
                currentTrackIndex = (currentTrackIndex - 1 + searchResults.Count) % searchResults.Count;
            }
            StartPlayback(searchResults[currentTrackIndex]);
        }

        public void HandleMediaEnded()
        {
            var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (repeatMode == 2 && currentTrackIndex >= 0)
                {
                    StartPlayback(searchResults[currentTrackIndex]);
                    return;
                }

                if (repeatMode == 1 || isShuffleOn)
                {
                    NextButton_Click(null, null);
                    return;
                }

                if (currentTrackIndex < searchResults.Count - 1)
                {
                    currentTrackIndex++;
                    StartPlayback(searchResults[currentTrackIndex]);
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

        private async void SaveTrackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Button button = sender as Button;
                if (button == null) return;

                AudioItem track = button.Tag as AudioItem;
                if (track == null) return;

                // Показываем индикатор загрузки
                ProgressRing saveProgress = new ProgressRing
                {
                    IsActive = true,
                    Height = 20,
                    Width = 20,
                    Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"]
                };

                // Запоминаем оригинальное содержимое кнопки
                var originalContent = button.Content;
                button.Content = saveProgress;

                bool result = await SaveTrackAsync(track);

                if (result)
                {
                    // При успехе показываем зеленую галочку
                    button.Content = new FontIcon
                    {
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Glyph = "\uE73E", // Символ галочки
                        FontSize = 16,
                        Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"]
                    };

                    // Небольшая информация для пользователя внизу экрана
                    StatusTextBlock.Text = $"Трек \"{track.Artist} - {track.Title}\" добавлен в вашу библиотеку";

                    // Возвращаем исходную иконку через 2 секунды
                    await Task.Delay(2000);
                }
                else
                {
                    // При ошибке возвращаем оригинальную иконку
                    button.Content = originalContent;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при обработке нажатия кнопки сохранения: {ex.Message}");
            }
        }

        private async Task<bool> SaveTrackAsync(AudioItem track)
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                string accessToken = localSettings.Values["AccessToken"] as string;
                object userIdObj = localSettings.Values["UserId"];
                string instance = localSettings.Values["Instance"] as string;

                if (string.IsNullOrEmpty(accessToken) || userIdObj == null || string.IsNullOrEmpty(instance))
                {
                    await new MessageDialog("Ошибка авторизации. Пожалуйста, войдите снова.").ShowAsync();
                    return false;
                }

                int userId = Convert.ToInt32(userIdObj);

                // Получаем ID и owner_id трека
                int audioId = track.Id;
                int ownerId = track.OwnerId;

                // Если у трека нет owner_id, пробуем получить его из unique_id
                if (ownerId == 0 && !string.IsNullOrEmpty(track.UniqueId))
                {
                    string[] parts = track.UniqueId.Split('_');
                    if (parts.Length >= 2)
                    {
                        int extractedOwnerId;
                        if (int.TryParse(parts[0], out extractedOwnerId))
                        {
                            ownerId = extractedOwnerId;
                        }

                        // Также проверяем, если ID в unique_id отличается от track.Id
                        if (parts.Length > 1)
                        {
                            int extractedAudioId;
                            if (int.TryParse(parts[1], out extractedAudioId))
                            {
                                audioId = extractedAudioId;
                            }
                        }
                    }
                }

                // Проверяем, что owner_id не равен 0
                if (ownerId == 0)
                {
                    await new MessageDialog("Невозможно добавить трек: отсутствует идентификатор владельца трека.").ShowAsync();
                    return false;
                }

                // Формируем URL запроса к API с обязательными параметрами owner_id и id
                string requestUrl = $"https://{instance}/method/audio.add?owner_id={ownerId}&audio_id={audioId}&access_token={accessToken}";

                System.Diagnostics.Debug.WriteLine($"Запрос на сохранение трека: {requestUrl}");

                var response = await httpClient.GetAsync(requestUrl);
                string jsonResponse = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Ответ API на сохранение: {jsonResponse}");

                JsonObject rootObject = JsonObject.Parse(jsonResponse);

                if (rootObject.ContainsKey("error"))
                {
                    JsonObject errorObj = rootObject.GetNamedObject("error");
                    string errorMsg = "Неизвестная ошибка";

                    if (errorObj.ContainsKey("error_msg"))
                        errorMsg = errorObj.GetNamedString("error_msg");
                    else if (errorObj.ContainsKey("error_code"))
                        errorMsg = $"Код ошибки: {errorObj.GetNamedNumber("error_code")}";

                    System.Diagnostics.Debug.WriteLine($"API вернул ошибку: {errorMsg}");
                    await new MessageDialog("Ошибка при добавлении трека: " + errorMsg).ShowAsync();
                    return false;
                }

                // Проверяем успешный результат
                if (rootObject.ContainsKey("response"))
                {
                    // API возвращает ID добавленного трека в ответе response
                    var responseValue = rootObject.GetNamedValue("response");

                    // Проверяем различные форматы успешного ответа
                    if (responseValue.ValueType == JsonValueType.Number && responseValue.GetNumber() > 0)
                    {
                        return true;
                    }
                    else if (responseValue.ValueType == JsonValueType.Boolean && responseValue.GetBoolean())
                    {
                        return true;
                    }
                    else if (responseValue.ValueType == JsonValueType.String)
                    {
                        // OpenVK возвращает строку вида "owner_id_audio_id"
                        string audioIdString = responseValue.GetString();
                        if (!string.IsNullOrEmpty(audioIdString))
                        {
                            System.Diagnostics.Debug.WriteLine($"Трек успешно добавлен с ID: {audioIdString}");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при выполнении API запроса audio.add: {ex.Message}");
                await new MessageDialog("Произошла ошибка при сохранении трека: " + ex.Message).ShowAsync();
                return false;
            }
        }

        private void SearchResultsListView_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                FrameworkElement trackElement = e.OriginalSource as FrameworkElement;
                while (trackElement != null && !(trackElement.DataContext is AudioItem))
                {
                    trackElement = VisualTreeHelper.GetParent(trackElement) as FrameworkElement;
                }

                if (trackElement != null)
                {
                    // Сохраняем выбранный трек в глобальной переменной
                    selectedTrackForMenu = trackElement.DataContext as AudioItem;

                    // Показываем меню в позиции долгого нажатия
                    TrackContextMenu.ShowAt(trackElement);
                    e.Handled = true;
                }
            }
        }

        private void SearchResultsListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            FrameworkElement trackElement = e.OriginalSource as FrameworkElement;
            while (trackElement != null && !(trackElement.DataContext is AudioItem))
            {
                trackElement = VisualTreeHelper.GetParent(trackElement) as FrameworkElement;
            }

            if (trackElement != null)
            {
                // Сохраняем выбранный трек в глобальной переменной
                selectedTrackForMenu = trackElement.DataContext as AudioItem;

                // Показываем меню в позиции клика
                TrackContextMenu.ShowAt(trackElement);
                e.Handled = true;
            }
        }


        private async void AddTrackMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Используем сохраненный трек из глобальной переменной
                if (selectedTrackForMenu == null) return;

                // Получаем правильные идентификаторы трека
                var identifiers = await GetAudioCorrectIdentifiersAsync(selectedTrackForMenu);
                if (identifiers == null)
                {
                    await new MessageDialog("Не удалось получить информацию о треке.").ShowAsync();
                    selectedTrackForMenu = null;
                    return;
                }

                int ownerId = identifiers.Item1;
                int audioId = identifiers.Item2;

                if (ownerId == 0 || audioId == 0)
                {
                    await new MessageDialog("Не удалось получить необходимые идентификаторы трека.").ShowAsync();
                    selectedTrackForMenu = null;
                    return;
                }

                // Добавляем трек в коллекцию
                bool success = await AddTrackToMyAudioAsync(ownerId, audioId);

                if (success)
                {
                    // Выводим сообщение об успешном добавлении
                    MessageDialog successDialog = new MessageDialog($"Трек \"{selectedTrackForMenu.Artist} - {selectedTrackForMenu.Title}\" добавлен в вашу коллекцию");
                    await successDialog.ShowAsync();
                }

                // Очищаем выбранный трек
                selectedTrackForMenu = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при добавлении трека: {ex.Message}");
                selectedTrackForMenu = null;
            }
        }

        private void ShareTrackMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Используем сохраненный трек из глобальной переменной
                if (selectedTrackForMenu == null) return;

                // Сохраняем информацию о треке в локальные переменные, 
                // чтобы избежать проблем с асинхронным обратным вызовом
                string artist = selectedTrackForMenu.Artist ?? "";
                string title = selectedTrackForMenu.Title ?? "";
                string trackInfo = $"{artist} - {title}";

                // Реализация функции поделиться
                DataTransferManager dataTransferManager = DataTransferManager.GetForCurrentView();

                // Отписываемся от существующих обработчиков, чтобы не было дублирования
                dataTransferManager.DataRequested -= DataTransferManager_DataRequested;

                // Добавляем обработчик с сохраненной информацией о треке
                dataTransferManager.DataRequested += (s, args) => {
                    DataRequest request = args.Request;
                    if (request != null)
                    {
                        request.Data.SetText(trackInfo);
                        request.Data.Properties.Title = "Поделиться треком";
                        request.Data.Properties.Description = $"Трек: {trackInfo}";
                    }
                };

                // Показываем интерфейс
                DataTransferManager.ShowShareUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при поделиться треком: {ex.Message}");
            }

            // Очищаем выбранный трек после запуска UI обмена,
            // а не до его завершения
            selectedTrackForMenu = null;
        }

        private void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            // Это только заглушка для отписки от события, реальный код в анонимном методе
        }

        private async Task<Tuple<int, int>> GetAudioCorrectIdentifiersAsync(AudioItem track)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Получение идентификаторов для трека: Title={track.Title}, Artist={track.Artist}");
                System.Diagnostics.Debug.WriteLine($"Id={track.Id}, OwnerId={track.OwnerId}, UniqueId={track.UniqueId ?? "null"}");

                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                string accessToken = localSettings.Values["AccessToken"] as string;
                object userIdObj = localSettings.Values["UserId"];
                string instance = localSettings.Values["Instance"] as string;

                if (string.IsNullOrEmpty(accessToken) || userIdObj == null || string.IsNullOrEmpty(instance))
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка: отсутствуют данные авторизации");
                    return null;
                }

                int userId = Convert.ToInt32(userIdObj);
                System.Diagnostics.Debug.WriteLine($"UserId из настроек: {userId}");

                // Получаем ID и owner_id трека
                int audioId = track.Id;
                int ownerId = track.OwnerId;
                string uniqueId = track.UniqueId ?? "";

                // Если у трека уже есть owner_id и audio_id, просто возвращаем их
                if (ownerId != 0 && audioId != 0)
                {
                    return new Tuple<int, int>(ownerId, audioId);
                }

                // Если у трека нет owner_id или audio_id, пробуем получить их через audio.getById
                System.Diagnostics.Debug.WriteLine("Выполняем запрос audio.getById для получения правильных идентификаторов");

                // Формируем ID для аудио в формате owner_id_audio_id
                string audioIds = "";

                // Если у нас есть UniqueId, используем его
                if (!string.IsNullOrEmpty(uniqueId))
                {
                    audioIds = uniqueId;
                    System.Diagnostics.Debug.WriteLine($"Используем UniqueId: {uniqueId}");
                }
                // Если нет UniqueId, но есть ownerId и audioId, формируем ID вручную
                else if (ownerId != 0 && audioId != 0)
                {
                    audioIds = $"{ownerId}_{audioId}";
                    System.Diagnostics.Debug.WriteLine($"Формируем ID вручную: {audioIds}");
                }
                // Если ничего не подходит, пробуем использовать только audioId и userId
                else if (audioId != 0)
                {
                    audioIds = $"{userId}_{audioId}";
                    System.Diagnostics.Debug.WriteLine($"Используем userId и audioId: {audioIds}");
                }

                // Если не удалось сформировать ID, возвращаем null
                if (string.IsNullOrEmpty(audioIds))
                {
                    System.Diagnostics.Debug.WriteLine("Не удалось сформировать ID для аудио");
                    return null;
                }

                // Формируем URL запроса к API для получения информации о треке
                string getByIdUrl = $"https://{instance}/method/audio.getById?audios={audioIds}&access_token={accessToken}";
                System.Diagnostics.Debug.WriteLine($"Запрос audio.getById: {getByIdUrl.Replace(accessToken, "***")}");

                // Выполняем запрос
                var getByIdResponse = await httpClient.GetAsync(getByIdUrl);
                string getByIdJsonResponse = await getByIdResponse.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Ответ audio.getById: {getByIdJsonResponse}");

                // Парсим ответ
                JsonObject getByIdObject = JsonObject.Parse(getByIdJsonResponse);

                // Проверяем на ошибки
                if (getByIdObject.ContainsKey("error"))
                {
                    string errorMsg = "Неизвестная ошибка";

                    if (getByIdObject.GetNamedObject("error").ContainsKey("error_msg"))
                        errorMsg = getByIdObject.GetNamedObject("error").GetNamedString("error_msg");
                    else if (getByIdObject.GetNamedObject("error").ContainsKey("error_code"))
                        errorMsg = $"Код ошибки: {getByIdObject.GetNamedObject("error").GetNamedNumber("error_code")}";

                    System.Diagnostics.Debug.WriteLine($"API вернул ошибку при audio.getById: {errorMsg}");
                    return null;
                }

                // Получаем данные о треке
                if (getByIdObject.ContainsKey("response"))
                {
                    JsonArray audioArray;

                    // Проверяем тип response - может быть массивом напрямую или объектом с полем items
                    if (getByIdObject.GetNamedValue("response").ValueType == JsonValueType.Array)
                    {
                        audioArray = getByIdObject.GetNamedArray("response");
                    }
                    else if (getByIdObject.GetNamedValue("response").ValueType == JsonValueType.Object &&
                              getByIdObject.GetNamedObject("response").ContainsKey("items"))
                    {
                        audioArray = getByIdObject.GetNamedObject("response").GetNamedArray("items");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Неизвестный формат ответа audio.getById");
                        return null;
                    }

                    // Проверяем, что в массиве есть элементы
                    if (audioArray.Count > 0)
                    {
                        JsonObject audioObject = audioArray.GetObjectAt(0);

                        // Получаем owner_id и id
                        if (audioObject.ContainsKey("owner_id") && audioObject.ContainsKey("id"))
                        {
                            ownerId = (int)audioObject.GetNamedNumber("owner_id");
                            audioId = (int)audioObject.GetNamedNumber("id");

                            System.Diagnostics.Debug.WriteLine($"Получены правильные идентификаторы: owner_id={ownerId}, id={audioId}");
                            return new Tuple<int, int>(ownerId, audioId);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("В ответе audio.getById отсутствуют owner_id или id");
                            return null;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Ответ audio.getById не содержит треков");
                        return null;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Ответ audio.getById не содержит поля response");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при получении идентификаторов трека: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> AddTrackToMyAudioAsync(int ownerId, int audioId)
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                string accessToken = localSettings.Values["AccessToken"] as string;
                object userIdObj = localSettings.Values["UserId"];
                string instance = localSettings.Values["Instance"] as string;

                if (string.IsNullOrEmpty(accessToken) || userIdObj == null || string.IsNullOrEmpty(instance))
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка: отсутствуют данные авторизации");
                    await new MessageDialog("Ошибка авторизации. Пожалуйста, войдите снова.").ShowAsync();
                    return false;
                }

                // Формируем URL запроса к API для добавления трека
                string requestUrl = $"https://{instance}/method/audio.add?owner_id={ownerId}&audio_id={audioId}&access_token={accessToken}";

                // Маскируем токен в логах
                string logUrl = $"https://{instance}/method/audio.add?owner_id={ownerId}&audio_id={audioId}&access_token=***";
                System.Diagnostics.Debug.WriteLine($"Запрос на добавление трека: {logUrl}");

                var response = await httpClient.GetAsync(requestUrl);
                string jsonResponse = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Ответ API на добавление: {jsonResponse}");

                JsonObject rootObject = JsonObject.Parse(jsonResponse);

                if (rootObject.ContainsKey("error"))
                {
                    string errorMsg = "Неизвестная ошибка";

                    if (rootObject.GetNamedObject("error").ContainsKey("error_msg"))
                        errorMsg = rootObject.GetNamedObject("error").GetNamedString("error_msg");
                    else if (rootObject.GetNamedObject("error").ContainsKey("error_code"))
                        errorMsg = $"Код ошибки: {rootObject.GetNamedObject("error").GetNamedNumber("error_code")}";

                    System.Diagnostics.Debug.WriteLine($"API вернул ошибку: {errorMsg}");
                    await new MessageDialog("Ошибка при добавлении трека: " + errorMsg).ShowAsync();
                    return false;
                }

                // Проверяем успешный результат - в случае audio.add API возвращает id добавленного трека
                if (rootObject.ContainsKey("response"))
                {
                    var responseValue = rootObject.GetNamedValue("response");

                    // В зависимости от формата ответа проверяем результат
                    if (responseValue.ValueType == JsonValueType.Number && responseValue.GetNumber() > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Успешное добавление трека: response={responseValue.GetNumber()} (id добавленного трека)");
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Неизвестный формат ответа: {responseValue.ValueType}");
                        return false;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Ответ API не содержит поля response");
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при выполнении API запроса audio.add: {ex.Message}");
                await new MessageDialog("Произошла ошибка при добавлении трека: " + ex.Message).ShowAsync();
                return false;
            }
        }
    }
}
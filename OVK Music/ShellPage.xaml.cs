using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using System.Collections.Generic;
using Windows.Storage;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Popups;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System;
using System.Net.Http;
using Windows.Data.Json;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Media;

namespace OVK_Music
{
    public sealed partial class ShellPage : Page
    {
        private HttpClient httpClient = new HttpClient();
        private UserProfile currentUser;
        private DispatcherTimer trackCountUpdateTimer;

        public ShellPage()
        {
            this.InitializeComponent();

            var items = new List<MenuItem>
    {
        new MenuItem { Title = "Треки", IconGlyph = "\uE8D6", Tag = "tracks" },
        new MenuItem { Title = "Плейлисты", IconGlyph = "\uE81E", Tag = "playlists" },
        new MenuItem { Title = "Поиск", IconGlyph = "\uE721", Tag = "search" },
        new MenuItem { Title = "Настройки", IconGlyph = "\uE713", Tag = "settings" }
    };
            MenuListView.ItemsSource = items;

            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values["AccessToken"] == null)
            {
                Frame rootFrame = Window.Current.Content as Frame;
                if (rootFrame != null)
                {
                    rootFrame.Navigate(typeof(MainPage));
                    return;
                }
            }

            // Подписываемся на события изменения коллекции треков
            EventManager.TracksCollectionChanged += EventManager_TracksCollectionChanged;

            // Настраиваем таймер для периодического обновления счетчика треков (каждые 30 секунд)
            trackCountUpdateTimer = new DispatcherTimer();
            trackCountUpdateTimer.Interval = TimeSpan.FromSeconds(30);
            trackCountUpdateTimer.Tick += TrackCountUpdateTimer_Tick;
            trackCountUpdateTimer.Start();

            LoadUserProfile();
            ContentFrame.Navigate(typeof(AudioListPage));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // Отписываемся от событий и останавливаем таймер при уходе со страницы
            EventManager.TracksCollectionChanged -= EventManager_TracksCollectionChanged;

            if (trackCountUpdateTimer != null)
            {
                trackCountUpdateTimer.Stop();
            }
        }

        private async void TrackCountUpdateTimer_Tick(object sender, object e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            string accessToken = localSettings.Values["AccessToken"] as string;
            string instance = localSettings.Values["Instance"] as string;
            int userId = (int)localSettings.Values["UserId"];

            if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(instance))
            {
                await LoadUserTracksCount(accessToken, instance, userId);
            }
        }

        private async void EventManager_TracksCollectionChanged(object sender, TracksCollectionChangedEventArgs e)
        {
            // Запускаем на UI потоке
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                // Если нам предоставлено новое количество треков, используем его
                if (e.ActionType == "refresh")
                {
                    // При полном обновлении используем значение из события
                    currentUser.TracksCount = e.NewCount;
                    UpdateTracksCountUI(e.NewCount);
                }
                else
                {
                    // Для операций добавления/удаления обновляем текущий счетчик
                    if (e.ActionType == "add")
                        currentUser.TracksCount++;
                    else if (e.ActionType == "remove")
                        currentUser.TracksCount--;

                    UpdateTracksCountUI(currentUser.TracksCount);
                }
            });
        }

        private async void LoadUserProfile()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                string accessToken = localSettings.Values["AccessToken"] as string;
                string instance = localSettings.Values["Instance"] as string;
                int userId = (int)localSettings.Values["UserId"];

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(instance))
                {
                    return;
                }

                string userApiUrl = $"https://{instance}/method/users.get?user_ids={userId}&fields=photo_100&v=5.131&access_token={accessToken}";

                var response = await httpClient.GetAsync(userApiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    JsonObject jsonObject = JsonObject.Parse(jsonResponse);

                    if (jsonObject.ContainsKey("response") && jsonObject["response"].ValueType == JsonValueType.Array)
                    {
                        JsonArray users = jsonObject["response"].GetArray();
                        if (users.Count > 0)
                        {
                            JsonObject user = users[0].GetObject();

                            currentUser = new UserProfile
                            {
                                UserId = userId,
                                DisplayName = GetJsonStringValue(user, "first_name") + " " + GetJsonStringValue(user, "last_name"),
                                AvatarUrl = GetJsonStringValue(user, "photo_100")
                            };

                            currentUser.LoadAvatar();

                            // Отобразить информацию о пользователе в UI
                            UserNameTextBlock.Text = currentUser.DisplayName;
                            UserAvatarBrush.ImageSource = currentUser.AvatarImage;
                            UserProfilePanel.Visibility = Visibility.Visible;

                            // Получить количество треков пользователя
                            await LoadUserTracksCount(accessToken, instance, userId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки профиля: {ex.Message}");
            }
        }

        private async Task LoadUserTracksCount(string accessToken, string instance, int userId)
        {
            try
            {
                string audioCountUrl = $"https://{instance}/method/audio.getCount?owner_id={userId}&v=5.131&access_token={accessToken}";

                var response = await httpClient.GetAsync(audioCountUrl);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    JsonObject jsonObject = JsonObject.Parse(jsonResponse);

                    if (jsonObject.ContainsKey("response"))
                    {
                        int tracksCount = (int)jsonObject["response"].GetNumber();
                        currentUser.TracksCount = tracksCount;

                        // Обновить UI с количеством треков
                        UpdateTracksCountUI(tracksCount);

                        // Сообщаем всем, что обновлено количество треков
                        EventManager.OnTracksCollectionChanged(tracksCount, "refresh");
                    }
                }
                else
                {
                    // Если нет доступа к методу getCount, попробуем получить через стандартный метод audio.get с count=0
                    string audioGetUrl = $"https://{instance}/method/audio.get?owner_id={userId}&count=0&v=5.131&access_token={accessToken}";

                    response = await httpClient.GetAsync(audioGetUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        JsonObject jsonObject = JsonObject.Parse(jsonResponse);

                        if (jsonObject.ContainsKey("response") && jsonObject["response"].ValueType == JsonValueType.Object)
                        {
                            JsonObject responseObj = jsonObject["response"].GetObject();
                            if (responseObj.ContainsKey("count"))
                            {
                                int tracksCount = (int)responseObj["count"].GetNumber();
                                currentUser.TracksCount = tracksCount;

                                // Обновить UI с количеством треков
                                UpdateTracksCountUI(tracksCount);

                                // Сообщаем всем, что обновлено количество треков
                                EventManager.OnTracksCollectionChanged(tracksCount, "refresh");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения количества треков: {ex.Message}");
            }
        }

        private void UpdateTracksCountUI(int tracksCount)
        {
            string tracksText = FormatTracksCount(tracksCount);
            TracksCountTextBlock.Text = tracksText;
        }

        private string FormatTracksCount(int count)
        {
            if (count % 10 == 1 && count % 100 != 11)
                return $"{count} трек";
            else if ((count % 10 >= 2 && count % 10 <= 4) && (count % 100 < 10 || count % 100 >= 20))
                return $"{count} трека";
            else
                return $"{count} треков";
        }

        private string GetJsonStringValue(JsonObject obj, string key, string defaultValue = "")
        {
            if (obj.ContainsKey(key))
            {
                JsonValue value = obj.GetNamedValue(key);
                if (value.ValueType == JsonValueType.String)
                    return value.GetString();
                return value.ToString();
            }
            return defaultValue;
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            MySplitView.IsPaneOpen = !MySplitView.IsPaneOpen;
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(SearchPage));
            MySplitView.IsPaneOpen = false;
        }

        private void MenuListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as MenuItem;
            switch (item.Tag)
            {
                case "tracks":
                    ContentFrame.Navigate(typeof(AudioListPage));
                    break;
                case "playlists":
                    ContentFrame.Navigate(typeof(PlaylistsPage));
                    break;
                case "search":
                    ContentFrame.Navigate(typeof(SearchPage));
                    break;
                case "settings":
                    ContentFrame.Navigate(typeof(SettingsPage));
                    break;
            }
            MySplitView.IsPaneOpen = false;
        }

        private void ContentFrame_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            if (e.SourcePageType == typeof(AudioListPage) || e.SourcePageType == typeof(PlaylistTracksPage))
            {
                AudioPlayerManager.CurrentAudioPlayerPage = null;
            }
        }
    }
}
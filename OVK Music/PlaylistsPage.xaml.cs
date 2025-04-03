using System;
using System.Collections.Generic;
using System.Net.Http;
using Windows.Data.Json;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OVK_Music
{
    public sealed partial class PlaylistsPage : Page
    {
        private HttpClient httpClient = new HttpClient();
        private List<PlaylistItem> playlistList = new List<PlaylistItem>();

        public PlaylistsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadPlaylistsAsync();
        }

        /// <summary>
        /// Универсальный метод для извлечения строкового значения из JSON.
        /// </summary>
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

        private async void LoadPlaylistsAsync()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            string accessToken = localSettings.Values["AccessToken"] as string;
            object userIdObj = localSettings.Values["UserId"];
            string instance = localSettings.Values["Instance"] as string;

            if (string.IsNullOrEmpty(accessToken) || userIdObj == null || string.IsNullOrEmpty(instance))
            {
                await new MessageDialog("Ошибка: отсутствуют данные авторизации. Пожалуйста, войдите в систему.").ShowAsync();
                return;
            }

            int userId = Convert.ToInt32(userIdObj);
            // URL для получения плейлистов
            string requestUrl = $"https://{instance}/method/audio.getAlbums?owner_id={userId}&access_token={accessToken}&offset=0";

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
                    await new MessageDialog("В ответе API отсутствует список плейлистов.").ShowAsync();
                    return;
                }

                JsonArray itemsArray = responseObject.GetNamedArray("items");
                playlistList.Clear();
                foreach (var item in itemsArray)
                {
                    if (item.ValueType == JsonValueType.Object)
                    {
                        JsonObject playlistObj = item.GetObject();
                        var playlist = new PlaylistItem
                        {
                            Id = (int)playlistObj.GetNamedNumber("id", 0),
                            OwnerId = (int)playlistObj.GetNamedNumber("owner_id", 0),
                            Title = GetJsonStringValue(playlistObj, "title", ""),
                            Description = GetJsonStringValue(playlistObj, "description", ""),
                            Size = (int)playlistObj.GetNamedNumber("size", 0),
                            Length = (int)playlistObj.GetNamedNumber("length", 0),
                            Created = (int)playlistObj.GetNamedNumber("created", 0),
                            Modified = playlistObj.ContainsKey("modified") &&
                                       playlistObj.GetNamedValue("modified").ValueType != JsonValueType.Null
                                       ? (int?)playlistObj.GetNamedNumber("modified", 0)
                                       : null,
                            Accessible = playlistObj.GetNamedBoolean("accessible", true),
                            Editable = playlistObj.GetNamedBoolean("editable", false),
                            Bookmarked = playlistObj.GetNamedBoolean("bookmarked", false),
                            Listens = (int)playlistObj.GetNamedNumber("listens", 0),
                            CoverUrl = GetJsonStringValue(playlistObj, "cover_url", ""),
                            Searchable = playlistObj.GetNamedBoolean("searchable", true)
                        };
                        playlistList.Add(playlist);
                    }
                }
                PlaylistsListView.ItemsSource = playlistList;
            }
            catch (Exception ex)
            {
                await new MessageDialog("Ошибка при загрузке плейлистов:\n" + ex.Message).ShowAsync();
            }
        }

        private void PlaylistsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            PlaylistItem selectedPlaylist = e.ClickedItem as PlaylistItem;
            if (selectedPlaylist != null)
            {
                Frame.Navigate(typeof(PlaylistTracksPage), selectedPlaylist);
            }
        }
    }
}

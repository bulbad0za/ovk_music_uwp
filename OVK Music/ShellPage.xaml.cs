using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using System.Collections.Generic;
using Windows.Storage;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Popups;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;

namespace OVK_Music
{
    public sealed partial class ShellPage : Page
    {
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

            ContentFrame.Navigate(typeof(AudioListPage));
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
using System.Collections.Generic;
using Windows.Storage;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using System;

namespace OVK_Music
{
    public sealed partial class SettingsPage : Page
    {
        private List<MenuItem> settingsItems = new List<MenuItem>();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            settingsItems.Clear();
            LoadSettings();
        }
        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Существующие пункты настроек
            settingsItems.Add(new MenuItem
            {
                Title = "Выйти из аккаунта",
                IconGlyph = "\uE750",
                Tag = "logout"
            });
            
            settingsItems.Add(new MenuItem
            {
                Title = "О приложении",
                IconGlyph = "\uE946",
                Tag = "about"
            });

            SettingsListView.ItemsSource = settingsItems;
        }

        private async void SettingsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as MenuItem;
            if (item == null)
                return;

            switch (item.Tag)
            {
                case "logout":
                    // Очистка данных авторизации
                    var localSettings = ApplicationData.Current.LocalSettings;
                    localSettings.Values.Remove("AccessToken");
                    localSettings.Values.Remove("UserId");
                    localSettings.Values.Remove("Instance");
                    localSettings.Values.Remove("TokenTimestamp");

                    Frame rootFrame = Window.Current.Content as Frame;
                    if (rootFrame != null)
                    {
                        rootFrame.Navigate(typeof(MainPage));
                    }
                    break;

                case "about":
                    string aboutText = "OVK Music\nВерсия: 0.1.2\n\nЭто приложение создано для прослушивания музыки из OpenVK-подобных инстанций. я сосал\n\nby Loroteam <3";
                    var dialog = new MessageDialog(aboutText, "О приложении");
                    await dialog.ShowAsync();
                    break;
            }
        }
    }
}

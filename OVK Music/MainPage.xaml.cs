using System;
using System.Net.Http;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.ViewManagement;

namespace OVK_Music
{
    public sealed partial class MainPage : Page
    {
        private const string CLIENT_NAME = "OVK Music Windows";
        private HttpClient httpClient;
        private bool isFirstLoginAttempt = true;
        private UISettings uiSettings;

        public MainPage()
        {
            this.InitializeComponent();
            httpClient = new HttpClient();

            // Инициализация отслеживания системной темы
            uiSettings = new UISettings();
            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;

            // Установка начальной темы
            UpdateTheme();
        }

        private async void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            // Так как событие может прийти из другого потока, используем диспетчер
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                UpdateTheme();
            });
        }

        private void UpdateTheme()
        {
            // Получаем текущую системную тему
            var foreground = uiSettings.GetColorValue(UIColorType.Foreground);

            // Если текст светлый - значит тема тёмная
            bool isDarkTheme = foreground.R > 128;

            // Устанавливаем соответствующую тему для приложения
            this.RequestedTheme = isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;
        }

        private void Show2FAButton_Click(object sender, RoutedEventArgs e)
        {
            CodeTextBox.Visibility = Visibility.Visible;
            Show2FAButton.Visibility = Visibility.Collapsed;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingProgressBar.Visibility = Visibility.Visible;
                LoginButton.IsEnabled = false;
                RegisterButton.IsEnabled = false;
                StatusTextBlock.Text = "Выполняется вход...";

                string selectedInstance = (InstanceComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
                if (string.IsNullOrEmpty(selectedInstance))
                {
                    StatusTextBlock.Text = "Выберите инстанс!";
                    return;
                }

                string domain = selectedInstance;
                string username = LoginTextBox.Text;
                string password = PasswordBox.Password;
                string code = CodeTextBox.Visibility == Visibility.Visible ? CodeTextBox.Text : null;

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    StatusTextBlock.Text = "Введите логин и пароль!";
                    return;
                }

                string tokenUrl = $"https://{domain}/token?username={Uri.EscapeDataString(username)}" +
                                $"&password={Uri.EscapeDataString(password)}" +
                                $"&grant_type=password" +
                                $"&client_name={Uri.EscapeDataString(CLIENT_NAME)}";

                if (!string.IsNullOrEmpty(code))
                {
                    tokenUrl += $"&code={Uri.EscapeDataString(code)}";
                }

                var response = await httpClient.GetAsync(tokenUrl);
                string jsonResponse = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    JsonObject jsonObject = JsonObject.Parse(jsonResponse);
                    string accessToken = jsonObject.GetNamedString("access_token");
                    int userId = (int)jsonObject.GetNamedNumber("user_id");

                    SaveAuthenticationData(accessToken, userId, domain);
                    StatusTextBlock.Text = $"Успешный вход! ID пользователя: {userId}";
                    Frame.Navigate(typeof(ShellPage));
                }
                else
                {
                    if (isFirstLoginAttempt)
                    {
                        Show2FAButton.Visibility = Visibility.Visible;
                        isFirstLoginAttempt = false;
                        StatusTextBlock.Text = "Ошибка входа. Возможно, у вас включена двухфакторная аутентификация?";
                    }
                    else
                    {
                        StatusTextBlock.Text = "Ошибка входа. Проверьте введенные данные.";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Произошла ошибка: {ex.Message}";
            }
            finally
            {
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                LoginButton.IsEnabled = true;
                RegisterButton.IsEnabled = true;
            }
        }

        private void SaveAuthenticationData(string accessToken, int userId, string instance)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["AccessToken"] = accessToken;
            localSettings.Values["UserId"] = userId;
            localSettings.Values["Instance"] = instance;
            localSettings.Values["TokenTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedInstance = (InstanceComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(selectedInstance))
            {
                StatusTextBlock.Text = "Выберите инстанс!";
                return;
            }

            var uri = new Uri($"https://{selectedInstance}/reg");
            Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}
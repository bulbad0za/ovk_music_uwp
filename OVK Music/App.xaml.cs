using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OVK_Music
{
    sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;

            // Инициализируем BackgroundMediaPlayer и подписываемся на его событие изменения состояния
            var bgPlayer = BackgroundMediaPlayer.Current;
            bgPlayer.CurrentStateChanged += Player_CurrentStateChanged;

            // Настраиваем SystemMediaTransportControls глобально
            var smtc = bgPlayer.SystemMediaTransportControls;
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;

            // Удаление регистрации обработчика кнопок из App.xaml.cs
            // smtc.ButtonPressed += Smtc_ButtonPressed;
        }

        private async void Player_CurrentStateChanged(MediaPlayer sender, object args)
        {
            // Если необходимо обновлять UI глобально, используйте Dispatcher:
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal, () =>
                {
                    // Например, можно логировать состояние или обновлять глобальные переменные
                    System.Diagnostics.Debug.WriteLine("Текущее состояние плеера: " + sender.CurrentState.ToString());
                });
        }

        // Метод оставляем для совместимости, но он больше не будет вызываться
        private async void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            // Пустой метод - все управление перенесено в AudioPlayerService
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            // Получаем корневой фрейм
            Frame rootFrame = Window.Current.Content as Frame;

            // Если фрейм ещё не создан, создаём его
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                // Здесь можно задать глобальные параметры фрейма
                Window.Current.Content = rootFrame;
            }

            // Проверяем наличие токена и выбираем страницу (MainPage для авторизации или ShellPage для основного контента)
            var localSettings = ApplicationData.Current.LocalSettings;
            bool isAuthenticated = localSettings.Values["AccessToken"] != null;

            if (!isAuthenticated)
            {
                rootFrame.Navigate(typeof(MainPage));
            }
            else
            {
                rootFrame.Navigate(typeof(ShellPage));
            }

            Window.Current.Activate();
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            // Сохраните состояние приложения и остановите фоновую активность, если требуется.
            deferral.Complete();
        }
    }
}
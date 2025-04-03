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
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
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
            smtc.ButtonPressed += Smtc_ButtonPressed;
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

        private async void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal, () =>
                {
                    switch (args.Button)
                    {
                        case SystemMediaTransportControlsButton.Play:
                            BackgroundMediaPlayer.Current.Play();
                            break;
                        case SystemMediaTransportControlsButton.Pause:
                            BackgroundMediaPlayer.Current.Pause();
                            break;
                        case SystemMediaTransportControlsButton.Next:
                            {
                                IAudioPlayerPage currentAudioPage = AudioListPage.CurrentAudioPlayerPage;
                                if (currentAudioPage == null)
                                {
                            // Если AudioListPage не активна, можно попробовать PlaylistTracksPage
                            // Например, если вы реализовали аналогичное свойство там:
                            // currentAudioPage = PlaylistTracksPage.CurrentAudioPlayerPage;
                        }
                                if (currentAudioPage != null)
                                {
                                    currentAudioPage.NextTrack();
                                }
                            }
                            break;
                        case SystemMediaTransportControlsButton.Previous:
                            {
                                IAudioPlayerPage currentAudioPage = AudioListPage.CurrentAudioPlayerPage;
                                if (currentAudioPage == null)
                                {
                            // Попытка получить PlaylistTracksPage, если требуется
                        }
                                if (currentAudioPage != null)
                                {
                                    currentAudioPage.PreviousTrack();
                                }
                            }
                            break;
                    }
                });
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
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

        /// <summary>
        /// Invoked when Navigation to a certain page fails.
        /// </summary>
        /// <param name="sender">The Frame which failed navigation.</param>
        /// <param name="e">Details about the navigation failure.</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            // Сохраните состояние приложения и остановите фоновую активность, если требуется.
            deferral.Complete();
        }
    }
}

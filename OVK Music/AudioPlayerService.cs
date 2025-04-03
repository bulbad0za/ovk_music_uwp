using System;
using System.Collections.Generic;
using Windows.Media.Playback;
using Windows.Media.Core;
using Windows.Media;
using Windows.Foundation;

namespace OVK_Music
{
    public class AudioPlayerService
    {
        private static AudioPlayerService _instance;
        public static AudioPlayerService Instance => _instance ?? (_instance = new AudioPlayerService());

        private MediaPlayer mediaPlayer;
        private string activePageId = "";

        public static string ActivePlaylistSource { get; private set; } = "";

        private List<AudioItem> currentPlaylist = new List<AudioItem>();
        private int currentIndex = -1;
        private bool isShuffleEnabled = false;
        private int repeatMode = 0;
        private string playlistSource = "";

        private TrackInfo currentTrackInfo = new TrackInfo();

        private string lastActivePlayerId = "";

        private string frozenPlaylistSource = "";
        private List<AudioItem> frozenPlaylist = new List<AudioItem>();
        private int frozenIndex = -1;

        public class TrackInfo
        {
            public string Title { get; set; } = "";
            public string Artist { get; set; } = "";
            public double Position { get; set; } = 0;
            public double Duration { get; set; } = 0;
            public bool IsPlaying { get; set; } = false;
        }

        private AudioPlayerService()
        {
            mediaPlayer = BackgroundMediaPlayer.Current;
            mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
            mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            
            mediaPlayer.CurrentStateChanged += (s, a) => 
            {
                System.Diagnostics.Debug.WriteLine($"Текущее состояние плеера: {s.CurrentState}");
            };
            
            try
            {
                var smtc = mediaPlayer.SystemMediaTransportControls;
                
                smtc.ButtonPressed -= Smtc_ButtonPressed;
                smtc.ButtonPressed += Smtc_ButtonPressed;
                
                smtc.IsEnabled = true;
                smtc.IsPlayEnabled = true;
                smtc.IsPauseEnabled = true;
                smtc.IsNextEnabled = true;
                smtc.IsPreviousEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка подписки на SMTC: {ex.Message}");
            }
        }

        private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            try
            {
                var ignored = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, 
                    () => 
                    {
                        System.Diagnostics.Debug.WriteLine($"SMTC Button Pressed: {args.Button}, ActivePlaylistSource: {ActivePlaylistSource}");
                        
                        switch (args.Button)
                        {
                            case SystemMediaTransportControlsButton.Play:
                                mediaPlayer.Play();
                                break;
                                
                            case SystemMediaTransportControlsButton.Pause:
                                mediaPlayer.Pause();
                                break;
                                
                            case SystemMediaTransportControlsButton.Next:
                                NextTrackFrozen();
                                break;
                                
                            case SystemMediaTransportControlsButton.Previous:
                                PreviousTrackFrozen();
                                break;
                        }
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обработки SMTC: {ex.Message}");
            }
        }

        public void SetActivePage(string pageId)
        {
            lastActivePlayerId = activePageId;
            activePageId = pageId;
            System.Diagnostics.Debug.WriteLine($"Активная страница плеера: {pageId}, предыдущая: {lastActivePlayerId}");
        }

        public string GetActivePage()
        {
            return activePageId;
        }

        public void SetCurrentTrack(string title, string artist)
        {
            currentTrackInfo.Title = title;
            currentTrackInfo.Artist = artist;
        }

        public TrackInfo GetCurrentTrackInfo()
        {
            var player = BackgroundMediaPlayer.Current;
            var session = player.PlaybackSession;

            currentTrackInfo.IsPlaying = player.CurrentState == MediaPlayerState.Playing;

            if (session != null)
            {
                currentTrackInfo.Position = session.Position.TotalSeconds;
                currentTrackInfo.Duration = session.NaturalDuration.TotalSeconds;
            }

            return currentTrackInfo;
        }

        public void SetCurrentPlaylist(List<AudioItem> playlist, int index, string source, bool shuffle, int repeat)
        {
            System.Diagnostics.Debug.WriteLine($"SetCurrentPlaylist вызван: source={source}, текущий activeSource={ActivePlaylistSource}");

            if (!string.IsNullOrEmpty(ActivePlaylistSource) && source != ActivePlaylistSource)
            {
                System.Diagnostics.Debug.WriteLine($"ОТКЛОНЕНО изменение источника с {ActivePlaylistSource} на {source}! Используйте ForceChangePlaylistSource для смены источника.");
                return;
            }

            if (string.IsNullOrEmpty(ActivePlaylistSource))
            {
                System.Diagnostics.Debug.WriteLine($"Установлен первичный активный источник: {source}");
                ActivePlaylistSource = source;
            }
            
            currentPlaylist = new List<AudioItem>(playlist);
            currentIndex = index;
            playlistSource = source;
            isShuffleEnabled = shuffle;
            repeatMode = repeat;
            
            frozenPlaylistSource = ActivePlaylistSource;
            frozenPlaylist = new List<AudioItem>(playlist);
            frozenIndex = index;
            
            System.Diagnostics.Debug.WriteLine($"Обновлены данные плейлиста: {source}, индекс={index}, frozenSource={frozenPlaylistSource}");
        }

        public void ForceChangePlaylistSource(string newSource)
        {
            string oldSource = ActivePlaylistSource;
            System.Diagnostics.Debug.WriteLine($"Принудительная смена источника с {oldSource} на {newSource}");
            ActivePlaylistSource = newSource;
            frozenPlaylistSource = newSource;
            
            mediaPlayer.Pause();
            mediaPlayer.Source = null;
        }

        public List<AudioItem> GetCurrentPlaylist()
        {
            return currentPlaylist;
        }

        public int GetCurrentIndex()
        {
            return currentIndex;
        }

        public void SetCurrentIndex(int index)
        {
            currentIndex = index;
        }

        public void NextTrack()
        {
            System.Diagnostics.Debug.WriteLine($"NextTrack вызван, source: {playlistSource}, currentIndex: {currentIndex}, playlist count: {currentPlaylist.Count}");

            if (currentPlaylist.Count == 0 || currentIndex < 0)
            {
                System.Diagnostics.Debug.WriteLine("Пустой плейлист или недопустимый индекс");
                return;
            }

            int newIndex = currentIndex;
            if (isShuffleEnabled)
            {
                Random rnd = new Random();
                newIndex = rnd.Next(currentPlaylist.Count);
            }
            else if (repeatMode == 1 || currentIndex < currentPlaylist.Count - 1)
            {
                newIndex = (currentIndex + 1) % currentPlaylist.Count;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Достигнут конец плейлиста без режима повтора");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Переключение с индекса {currentIndex} на {newIndex}, источник: {playlistSource}");
            
            currentIndex = newIndex;
            var newTrack = currentPlaylist[currentIndex];

            PlayTrack(newTrack);

            var page = AudioPlayerManager.CurrentAudioPlayerPage;
            if (page != null)
            {
                page.UpdateActiveTrack(currentIndex, playlistSource);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Активная страница не найдена для обновления UI");
            }
        }

        public void PreviousTrack()
        {
            System.Diagnostics.Debug.WriteLine($"PreviousTrack вызван, source: {playlistSource}, currentIndex: {currentIndex}, playlist count: {currentPlaylist.Count}");

            if (currentPlaylist.Count == 0 || currentIndex < 0) return;

            if (isShuffleEnabled)
            {
                Random rnd = new Random();
                currentIndex = rnd.Next(currentPlaylist.Count);
            }
            else if (repeatMode == 1 || currentIndex > 0)
            {
                currentIndex = (currentIndex - 1 + currentPlaylist.Count) % currentPlaylist.Count;
            }
            else
            {
                mediaPlayer.Position = TimeSpan.Zero;
                mediaPlayer.Play();
                return;
            }

            var newTrack = currentPlaylist[currentIndex];

            PlayTrack(newTrack);

            var page = AudioPlayerManager.CurrentAudioPlayerPage;
            if (page != null)
            {
                page.UpdateActiveTrack(currentIndex, playlistSource);
            }
        }

        private void PlayTrack(AudioItem track)
        {
            try
            {
                SetCurrentTrack(track.Title, track.Artist);
                
                System.Diagnostics.Debug.WriteLine($"Воспроизведение трека: {track.Title} из источника: {playlistSource}");
                
                mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(track.Url));
                mediaPlayer.Play();
                
                var smtc = mediaPlayer.SystemMediaTransportControls;
                smtc.IsEnabled = true;
                smtc.IsPlayEnabled = true;
                smtc.IsPauseEnabled = true;
                smtc.IsNextEnabled = true;
                smtc.IsPreviousEnabled = true;
                
                smtc.DisplayUpdater.Type = Windows.Media.MediaPlaybackType.Music;
                smtc.DisplayUpdater.MusicProperties.Title = track.Title;
                smtc.DisplayUpdater.MusicProperties.Artist = track.Artist;
                smtc.DisplayUpdater.Update();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка воспроизведения: {ex.Message}");
            }
        }
        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            if (repeatMode == 2 && currentIndex >= 0 && currentIndex < currentPlaylist.Count)
            {
                PlayTrack(currentPlaylist[currentIndex]);
            }
            else if (repeatMode == 1 || isShuffleEnabled)
            {
                if (isShuffleEnabled)
                {
                    Random rnd = new Random();
                    currentIndex = rnd.Next(currentPlaylist.Count);
                }
                else
                {
                    currentIndex = (currentIndex + 1) % currentPlaylist.Count;
                }

                if (currentIndex >= 0 && currentIndex < currentPlaylist.Count)
                {
                    PlayTrack(currentPlaylist[currentIndex]);

                    var page = AudioPlayerManager.CurrentAudioPlayerPage;
                    if (page != null)
                    {
                        page.UpdateActiveTrack(currentIndex, playlistSource);
                    }
                }
            }
            else if (currentIndex < currentPlaylist.Count - 1)
            {
                currentIndex++;
                if (currentIndex >= 0 && currentIndex < currentPlaylist.Count)
                {
                    PlayTrack(currentPlaylist[currentIndex]);

                    var page = AudioPlayerManager.CurrentAudioPlayerPage;
                    if (page != null)
                    {
                        page.UpdateActiveTrack(currentIndex, playlistSource);
                    }
                }
            }
            else
            {
                StopPlayback();

                var page = AudioPlayerManager.CurrentAudioPlayerPage;
                if (page != null)
                {
                    page.PlaybackStopped();
                }
            }
        }

        public void StopPlayback()
        {
            mediaPlayer.Pause();
            mediaPlayer.Source = null;
        }

        public bool IsShuffleEnabled()
        {
            return isShuffleEnabled;
        }

        public int GetRepeatMode()
        {
            return repeatMode;
        }

        private void NextTrackFrozen()
        {
            System.Diagnostics.Debug.WriteLine($"NextTrackFrozen вызван, source: {frozenPlaylistSource}, index: {frozenIndex}, count: {frozenPlaylist.Count}");
            
            if (frozenPlaylist.Count == 0 || frozenIndex < 0) return;
            
            int oldIndex = frozenIndex;
            
            int newIndex;
            if (isShuffleEnabled)
            {
                Random rnd = new Random();
                newIndex = rnd.Next(frozenPlaylist.Count);
            }
            else if (repeatMode == 1 || frozenIndex < frozenPlaylist.Count - 1)
            {
                newIndex = (frozenIndex + 1) % frozenPlaylist.Count;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Достигнут конец плейлиста без режима повтора");
                return;
            }
            
            frozenIndex = newIndex;
            currentIndex = newIndex;
            
            System.Diagnostics.Debug.WriteLine($"Переключение с индекса {oldIndex} на {newIndex} в замороженном плейлисте {frozenPlaylistSource}");
            
            playlistSource = frozenPlaylistSource;
            
            var newTrack = frozenPlaylist[frozenIndex];
            
            System.Diagnostics.Debug.WriteLine($"Воспроизведение трека (frozen): {newTrack.Title} из {frozenPlaylistSource}");
            PlayTrack(newTrack);
            
            var page = AudioPlayerManager.CurrentAudioPlayerPage;
            if (page != null)
            {
                page.UpdateActiveTrack(frozenIndex, frozenPlaylistSource);
            }
            else 
            {
                System.Diagnostics.Debug.WriteLine("Не найдена активная страница для обновления");
            }
        }

        private void PreviousTrackFrozen()
        {
            System.Diagnostics.Debug.WriteLine($"PreviousTrackFrozen вызван, source: {frozenPlaylistSource}, index: {frozenIndex}, count: {frozenPlaylist.Count}");
            
            if (frozenPlaylist.Count == 0 || frozenIndex < 0) return;
            
            int oldIndex = frozenIndex;
            
            int newIndex;
            if (isShuffleEnabled)
            {
                Random rnd = new Random();
                newIndex = rnd.Next(frozenPlaylist.Count);
            }
            else if (repeatMode == 1 || frozenIndex > 0)
            {
                newIndex = (frozenIndex - 1 + frozenPlaylist.Count) % frozenPlaylist.Count;
            }
            else
            {
                mediaPlayer.Position = TimeSpan.Zero;
                mediaPlayer.Play();
                return;
            }
            
            frozenIndex = newIndex;
            currentIndex = newIndex;
            
            System.Diagnostics.Debug.WriteLine($"Переключение с индекса {oldIndex} на {newIndex} в замороженном плейлисте {frozenPlaylistSource}");
            
            playlistSource = frozenPlaylistSource;
            
            var newTrack = frozenPlaylist[frozenIndex];
            
            System.Diagnostics.Debug.WriteLine($"Воспроизведение трека (frozen): {newTrack.Title} из {frozenPlaylistSource}");
            PlayTrack(newTrack);
            
            var page = AudioPlayerManager.CurrentAudioPlayerPage;
            if (page != null)
            {
                page.UpdateActiveTrack(frozenIndex, frozenPlaylistSource);
            }
            else 
            {
                System.Diagnostics.Debug.WriteLine("Не найдена активная страница для обновления");
            }
        }

        public string GetPlaylistSource()
        {
            return ActivePlaylistSource;
        }

        public void UpdatePlaybackSettings(bool shuffle, int repeat)
        {
            isShuffleEnabled = shuffle;
            repeatMode = repeat;
            System.Diagnostics.Debug.WriteLine($"Обновлены настройки воспроизведения: shuffle={shuffle}, repeat={repeat}");
        }
    }
}
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OVK_Music
{
    public static class AudioPlayerManager
    {
        private static IAudioPlayerPage _currentAudioPlayerPage;

        public static IAudioPlayerPage CurrentAudioPlayerPage
        {
            get { return _currentAudioPlayerPage; }
            set
            {
                _currentAudioPlayerPage = value;
                System.Diagnostics.Debug.WriteLine($"AudioPlayerPage установлена: {value?.GetType().Name ?? "null"}");
            }
        }
    }

    public interface IAudioPlayerPage
    {
        void UpdateActiveTrack(int index, string source);
        void PlaybackStopped();
        void NextTrack();
        void PreviousTrack();
    }
}
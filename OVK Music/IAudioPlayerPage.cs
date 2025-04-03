using System;

namespace OVK_Music
{
    public interface IAudioPlayerPage
    {
        void UpdatePlaybackStatus(bool playing, string trackInfo);
        void NextTrack();
        void PreviousTrack();
        void HandleMediaEnded();
        void UpdateActiveTrack(int index, string source);
        void PlaybackStopped();
    }

    public static class AudioPlayerManager
    {
        public static IAudioPlayerPage CurrentAudioPlayerPage { get; set; }
    }
}
using System;

namespace OVK_Music
{
    public static class EventManager
    {
        // Событие, которое срабатывает при изменении коллекции треков
        public static event EventHandler<TracksCollectionChangedEventArgs> TracksCollectionChanged;

        // Метод для вызова события
        public static void OnTracksCollectionChanged(int newCount, string actionType)
        {
            TracksCollectionChanged?.Invoke(null, new TracksCollectionChangedEventArgs
            {
                NewCount = newCount,
                ActionType = actionType
            });
        }
    }

    // Аргументы события
    public class TracksCollectionChangedEventArgs : EventArgs
    {
        public int NewCount { get; set; }
        public string ActionType { get; set; } // например: "add", "remove", "refresh"
    }
}
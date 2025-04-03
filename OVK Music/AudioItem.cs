using System;

namespace OVK_Music
{
    public class AudioItem
    {
        public string UniqueId { get; set; }
        public int Id { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public int Duration { get; set; }
        public string Url { get; set; }

        public string CoverUrl { get; set; }

        public string Manifest { get; set; }
        public string[] Keys { get; set; }

        public string DurationFormatted
        {
            get
            {
                TimeSpan ts = TimeSpan.FromSeconds(Duration);
                return ts.ToString(@"mm\:ss");
            }
        }
    }
}

using System;
using Windows.UI.Xaml.Media.Imaging;

namespace OVK_Music
{
    public class UserProfile
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public BitmapImage AvatarImage { get; set; }
        public int TracksCount { get; set; }

        public UserProfile()
        {
            AvatarImage = new BitmapImage();
        }

        public void LoadAvatar()
        {
            if (!string.IsNullOrEmpty(AvatarUrl))
            {
                AvatarImage.UriSource = new Uri(AvatarUrl);
            }
        }
    }
}
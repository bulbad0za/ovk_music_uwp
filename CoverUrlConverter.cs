using System;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace OVK_Music
{
    public class CoverUrlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string url = value as string;
            if (string.IsNullOrEmpty(url))
            {
                return new BitmapImage(new Uri("ms-appx:///Assets/placeholder.png"));
            }

            if (url == "/assets/packages/static/openvk/img/song.jpg")
            {
                return new BitmapImage(new Uri("ms-appx:///Assets/placeholder.png"));
            }

            try
            {
                return new BitmapImage(new Uri(url));
            }
            catch
            {
                return new BitmapImage(new Uri("ms-appx:///Assets/placeholder.png"));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}

using System;
using Windows.UI.Xaml.Data;

namespace OVK_Music
{
    public class IntToTrackCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int)
            {
                int count = (int)value;
                string suffix = count == 1 ? "трек" : (count >= 2 && count <= 4) ? "трека" : "треков";
                return $"{count} {suffix}";
            }
            return "0 треков";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
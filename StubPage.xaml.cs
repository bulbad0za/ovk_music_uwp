using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OVK_Music
{
    public sealed partial class StubPage : Page
    {
        public StubPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string title = e.Parameter as string;
            TitleTextBlock.Text = title;
        }
    }
}

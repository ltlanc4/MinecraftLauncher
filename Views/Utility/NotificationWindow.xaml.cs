using System.Windows;
using System.Windows.Threading;

namespace MinecraftLauncher
{
    public partial class NotificationWindow : Window
    {
        private DispatcherTimer _closeTimer;

        // Constructor mặc định (bắt buộc cho WPF)
        public NotificationWindow()
        {
            InitializeComponent();
        }

        // Constructor tùy chỉnh để nạp nội dung
        public NotificationWindow(string title, string message) : this()
        {
            txtTitle.Text = title;
            txtMessage.Text = message;

            // Khởi tạo Timer tự đóng sau 4 giây
            _closeTimer = new DispatcherTimer();
            _closeTimer.Interval = TimeSpan.FromSeconds(4);
            _closeTimer.Tick += CloseTimer_Tick;
            _closeTimer.Start();
        }

        private void CloseTimer_Tick(object sender, EventArgs e)
        {
            _closeTimer.Stop();
            this.Close(); // Có thể thêm FadeOut animation trước khi close ở đây
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_closeTimer != null) _closeTimer.Stop();
            this.Close();
        }
    }
}
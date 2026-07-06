using System;
using System.Windows;
using System.Windows.Media.Animation;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher
{
    public partial class NotificationWindow : Window
    {
        private bool _isClosing = false;

        public NotificationWindow(NotificationViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;

            // Lắng nghe lệnh đóng để chạy hiệu ứng
            viewModel.RequestClose += CloseWithAnimation;
        }

        private void CloseWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;

            // Hiệu ứng FadeOut được giữ lại ở Code-Behind vì nó là xử lý Đồ họa UI
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.25)
            };

            fadeOut.Completed += (s, e) => this.Close();
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
        }
    }
}
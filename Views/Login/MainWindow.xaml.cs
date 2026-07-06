using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher
{
    public partial class MainWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private FrameworkElement _currentPanel;

        public MainWindow()
        {
            InitializeComponent();
            
            // Khởi tạo và gắn kết bộ não điều khiển dữ liệu
            _viewModel = new LoginViewModel();
            this.DataContext = _viewModel;
            _currentPanel = LoginPanel;

            // Đăng ký sự kiện lắng nghe từ ViewModel để kích hoạt Animation UI mượt mà
            _viewModel.OnPanelChanged += ViewModel_OnPanelChanged;
            _viewModel.OnLoginSuccess += ViewModel_OnLoginSuccess;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            this.MouseLeftButtonDown += (s, e) => this.DragMove();
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_viewModel.IsLoading))
            {
                var storyboard = LoadingOverlay.Resources["SpinAnimation"] as Storyboard;
                if (_viewModel.IsLoading) storyboard?.Begin();
                else storyboard?.Stop();
            }
        }

        private void ViewModel_OnPanelChanged(ActivePanelState targetState, bool slideRight)
        {
            if (targetState == ActivePanelState.Login)
            {
                txtRegPassword.Password = string.Empty;
                txtRegConfirm.Password = string.Empty;
                txtRecoveryNewPass.Password = string.Empty;
                txtRecoveryConfirmPass.Password = string.Empty;
            }

            FrameworkElement toPanel = null;

            switch (targetState)
            {
                case ActivePanelState.Login:
                    toPanel = LoginPanel;
                    break;
                case ActivePanelState.Register:
                    toPanel = RegisterPanel;
                    break;
                case ActivePanelState.ForgotPasswordStep1:
                    toPanel = ForgotPasswordPanel;
                    pnlRecoveryStep1.Visibility = Visibility.Visible;
                    pnlRecoveryStep2.Visibility = Visibility.Collapsed;
                    break;
                case ActivePanelState.ForgotPasswordStep2:
                    toPanel = ForgotPasswordPanel;
                    pnlRecoveryStep1.Visibility = Visibility.Collapsed;
                    pnlRecoveryStep2.Visibility = Visibility.Visible;
                    break;
            }

            if (toPanel != null)
            {
                AnimateTransition(_currentPanel, toPanel, slideRight);
                _currentPanel = toPanel;
            }
        }

        private void ViewModel_OnLoginSuccess()
        {
            this.IsHitTestVisible = false;
            var fadeOutWindow = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fadeOutWindow.Completed += (s, e) =>
            {
                try
                {
                    // 1. Khởi tạo sảnh chính
                    HomeWindow home = new HomeWindow(_viewModel.LoginUsername, "fake-token", "fake-uuid");
                    
                    // 2. CHUYỂN GIAO QUYỀN CỬA SỔ CHÍNH: Ngăn WPF tự tắt toàn bộ App khi close MainWindow
                    Application.Current.MainWindow = home;
                    
                    home.Opacity = 0;
                    home.Show();

                    var fadeInHome = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
                    home.BeginAnimation(Window.OpacityProperty, fadeInHome);

                    // 3. Đóng cửa sổ đăng nhập
                    this.Close();
                }
                catch (Exception ex)
                {
                    // NẾU CÓ BẢNG NÀY HIỆN LÊN: Tức là file HomeWindow.xaml của bạn vẫn còn sót chữ Click="..."
                    MessageBox.Show($"Phát hiện lỗi XAML tại HomeWindow:\n{ex.Message}\n\nChi tiết: {ex.InnerException?.Message}", 
                                    "Lỗi khởi tạo sảnh chính", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            this.BeginAnimation(Window.OpacityProperty, fadeOutWindow);
        }

        private void AnimateTransition(FrameworkElement fromPanel, FrameworkElement toPanel, bool slideRight)
        {
            if (fromPanel == null || toPanel == null || fromPanel == toPanel) return;

            this.IsHitTestVisible = false;
            Thickness outTargetMargin = slideRight ? new Thickness(30, 0, -30, 0) : new Thickness(-30, 0, 30, 0);
            Thickness inStartMargin = slideRight ? new Thickness(-30, 0, 30, 0) : new Thickness(30, 0, -30, 0);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            var slideOut = new ThicknessAnimation(new Thickness(0), outTargetMargin, TimeSpan.FromMilliseconds(150));

            fadeOut.Completed += (s, e) =>
            {
                fromPanel.Visibility = Visibility.Collapsed;
                toPanel.Opacity = 0;
                toPanel.Margin = inStartMargin;
                toPanel.Visibility = Visibility.Visible;

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                var slideIn = new ThicknessAnimation(inStartMargin, new Thickness(0), TimeSpan.FromMilliseconds(200));
                fadeIn.Completed += (s2, e2) => this.IsHitTestVisible = true;

                toPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                toPanel.BeginAnimation(FrameworkElement.MarginProperty, slideIn);
            };

            fromPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            fromPanel.BeginAnimation(FrameworkElement.MarginProperty, slideOut);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_viewModel.IsLoading)
            {
                if (_viewModel.CurrentPanel == ActivePanelState.Login) _viewModel.LoginCommand.Execute(txtLoginPassword);
                else if (_viewModel.CurrentPanel == ActivePanelState.Register) _viewModel.RegisterCommand.Execute(new object[] { txtRegPassword, txtRegConfirm });
                else if (_viewModel.CurrentPanel == ActivePanelState.ForgotPasswordStep1) _viewModel.SendRecoveryCodeCommand.Execute(null);
                else if (_viewModel.CurrentPanel == ActivePanelState.ForgotPasswordStep2) _viewModel.ConfirmResetPasswordCommand.Execute(new object[] { txtRecoveryNewPass, txtRecoveryConfirmPass });
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
    }

    // Bộ chuyển đổi đa tham số hỗ trợ gom nhóm PasswordBox truyền vào Command ngầm
    public class ParamConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) => values.Clone();
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
}
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace MinecraftLauncher
{
    public partial class MainWindow : Window
    {
        private FrameworkElement _currentPanel;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // ĐỊA CHỈ BACKEND API
        private readonly string API_BASE_URL = "http://180.93.43.73:3000/auth";

        // === THÊM: TÊN TỆP LƯU TRỮ PHIÊN ĐĂNG NHẬP ===
        private readonly string SESSION_FILE = "session_data.json";

        public MainWindow()
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => this.DragMove();
            _currentPanel = LoginPanel;
            
            // === THÊM: Lắng nghe sự kiện để tự động điền tài khoản khi mở Launcher ===
            this.Loaded += MainWindow_Loaded;
        }

        // ================= TỰ ĐỘNG ĐIỀN VÀ ĐĂNG NHẬP NẾU CÓ LƯU =================
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists(SESSION_FILE))
            {
                try
                {
                    string json = File.ReadAllText(SESSION_FILE);
                    var session = JsonSerializer.Deserialize<UserSession>(json);

                    if (session != null && !string.IsNullOrEmpty(session.Username) && !string.IsNullOrEmpty(session.Password))
                    {
                        // Điền sẵn thông tin vào giao diện (Dùng đúng tên biến txtLogin của bạn)
                        txtLoginUsername.Text = session.Username;
                        
                        // Giải mã password từ Base64 để điền vào ô mật khẩu
                        string decodedPass = Encoding.UTF8.GetString(Convert.FromBase64String(session.Password));
                        txtLoginPassword.Password = decodedPass;
                        
                        chkRememberMe.IsChecked = true;

                        // Tự động kích hoạt Đăng Nhập
                        LoginButton_Click(btnLogin, null);
                    }
                }
                catch
                {
                    File.Delete(SESSION_FILE);
                }
            }
        }

        // ================= XỬ LÝ NHẤN PHÍM ENTER =================
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_currentPanel == LoginPanel)
                {
                    LoginButton_Click(btnLogin, null);
                }
                else if (_currentPanel == RegisterPanel)
                {
                    RegisterButton_Click(btnRegister, null);
                }
                else if (_currentPanel == ForgotPasswordPanel)
                {
                    if (pnlRecoveryStep1.Visibility == Visibility.Visible)
                        SendRecoveryCode_Click(btnSendCode, null);
                    else
                        ConfirmResetPassword_Click(btnConfirmReset, null);
                }
            }
        }

        // ================= XỬ LÝ NÚT ĐIỀU KHIỂN CỬA SỔ =================
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // ================= HIỆU ỨNG LOADING SPINNER =================
        private void ShowLoading(bool show)
        {
            var storyboard = LoadingOverlay.Resources["SpinAnimation"] as Storyboard;
            if (show)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                storyboard?.Begin();
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
            else
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    storyboard?.Stop();
                };
                LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        // ================= HIỆU ỨNG CHUYỂN CẢNH (ANIMATION) =================
        private void AnimateTransition(FrameworkElement fromPanel, FrameworkElement toPanel, bool slideRight = false)
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

        // ================= ĐIỀU HƯỚNG CÁC PANEL =================
        private void SwitchToRegister_MouseDown(object sender, MouseButtonEventArgs e)
        {
            AnimateTransition(_currentPanel, RegisterPanel);
            _currentPanel = RegisterPanel;
        }

        private void SwitchToForgotPass_MouseDown(object sender, MouseButtonEventArgs e)
        {
            pnlRecoveryStep1.Visibility = Visibility.Visible;
            pnlRecoveryStep1.BeginAnimation(UIElement.OpacityProperty, null);
            pnlRecoveryStep1.Opacity = 1;
            pnlRecoveryStep1.BeginAnimation(FrameworkElement.MarginProperty, null);
            pnlRecoveryStep1.Margin = new Thickness(0);
            pnlRecoveryStep2.Visibility = Visibility.Collapsed;

            AnimateTransition(_currentPanel, ForgotPasswordPanel);
            _currentPanel = ForgotPasswordPanel;
        }

        private void ReturnToLogin()
        {
            AnimateTransition(_currentPanel, LoginPanel, true);
            _currentPanel = LoginPanel;
            
            txtRegUsername.Text = "";
            txtRegEmail.Text = "";
            txtRegPassword.Password = "";
            txtRegConfirm.Password = "";
            
            txtRecoveryUsername.Text = "";
            txtRecoveryEmail.Text = "";
            txtRecoveryCode.Text = "";
            txtRecoveryNewPass.Password = "";
            txtRecoveryConfirmPass.Password = "";
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            ReturnToLogin();
        }

        private void EnsureJsonResponse(string responseString)
        {
            if (string.IsNullOrWhiteSpace(responseString) || !responseString.Trim().StartsWith("{"))
            {
                throw new Exception("Máy chủ không trả về JSON hợp lệ. Hãy kiểm tra xem Node.js đã bật chưa.");
            }
        }

        // ================= XỬ LÝ ĐĂNG NHẬP =================
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = txtLoginUsername.Text;
            string password = txtLoginPassword.Password;
            
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập tài khoản và mật khẩu!");
                return;
            }

            try
            {
                ShowLoading(true);
                await Task.Delay(1500); 

                var payload = new { username = username, password = password };
                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{API_BASE_URL}/login", content);
                string responseString = await response.Content.ReadAsStringAsync();
                
                EnsureJsonResponse(responseString);

                var result = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response.IsSuccessStatusCode && result != null && result.Success)
                {
                    // === THÊM: LƯU TÀI KHOẢN & MẬT KHẨU (ĐÃ MÃ HÓA) NẾU CÓ TICK ===
                    if (chkRememberMe.IsChecked == true)
                    {
                        var newSession = new UserSession
                        {
                            Username = username,
                            Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password))
                        };
                        File.WriteAllText(SESSION_FILE, JsonSerializer.Serialize(newSession));
                    }
                    else
                    {
                        if (File.Exists(SESSION_FILE)) File.Delete(SESSION_FILE);
                    }
                    // ==============================================================

                    this.IsHitTestVisible = false;

                    var fadeOutWindow = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                    fadeOutWindow.Completed += (s3, e3) =>
                    {
                        HomeWindow home = new HomeWindow(result.Username, result.Token, result.Uuid);
                        home.Opacity = 0; 
                        home.Show();

                        var fadeInHome = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
                        home.BeginAnimation(Window.OpacityProperty, fadeInHome);

                        this.Close();
                    };
                    this.BeginAnimation(Window.OpacityProperty, fadeOutWindow);
                }
                else
                {
                    ShowLoading(false);
                    NotificationManager.Show("LỖI", result?.Message ?? "Tài khoản hoặc mật khẩu sai!");
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                NotificationManager.Show("LỖI KẾT NỐI", ex.Message);
            }
        }

        // ================= XỬ LÝ ĐĂNG KÝ =================
        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string username = txtRegUsername.Text;
            string email = txtRegEmail.Text;
            string pass = txtRegPassword.Password;
            string confirm = txtRegConfirm.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass))
            {
                NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập đầy đủ thông tin!");
                return;
            }

            if (pass != confirm)
            {
                NotificationManager.Show("LỖI", "Mật khẩu xác nhận không khớp!");
                return;
            }

            if (!(sender is Button btn)) return;

            try
            {
                btn.IsEnabled = false;
                btn.Content = "ĐANG TẠO...";

                await Task.Delay(1500);

                var payload = new { username = username, email = email, password = pass };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{API_BASE_URL}/register", content);
                string responseString = await response.Content.ReadAsStringAsync();

                EnsureJsonResponse(responseString);

                var result = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response.IsSuccessStatusCode && result != null && result.Success)
                {
                    NotificationManager.Show("THÀNH CÔNG", $"Tạo tài khoản {username} thành công!");
                    ReturnToLogin(); 
                }
                else
                {
                    NotificationManager.Show("LỖI", result?.Message ?? "Tên tài khoản hoặc Email đã tồn tại!");
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show("LỖI KẾT NỐI", ex.Message);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "TẠO TÀI KHOẢN";
            }
        }

        // ================= XỬ LÝ QUÊN MẬT KHẨU =================
        private async void SendRecoveryCode_Click(object sender, RoutedEventArgs e)
        {
            string username = txtRecoveryUsername.Text;
            string email = txtRecoveryEmail.Text;
            
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
            {
                NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập Tên tài khoản và Email!");
                return;
            }

            if (!(sender is Button btn)) return;

            try
            {
                btn.IsEnabled = false;
                btn.Content = "ĐANG GỬI MÃ...";

                await Task.Delay(1500);

                var payload = new { username = username, email = email };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{API_BASE_URL}/forgot-password", content);
                string responseString = await response.Content.ReadAsStringAsync();

                EnsureJsonResponse(responseString);

                var result = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response.IsSuccessStatusCode && result != null && result.Success)
                {
                    NotificationManager.Show("GỬI MÃ OTP", $"Mã xác nhận đã được gửi đến email: {email}");
                    AnimateTransition(pnlRecoveryStep1, pnlRecoveryStep2);
                }
                else
                {
                    NotificationManager.Show("LỖI", result?.Message ?? "Thông tin tài khoản không hợp lệ!");
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show("LỖI KẾT NỐI", ex.Message);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "GỬI MÃ KHÔI PHỤC";
            }
        }

        private async void ConfirmResetPassword_Click(object sender, RoutedEventArgs e)
        {
            string username = txtRecoveryUsername.Text; 
            string code = txtRecoveryCode.Text;
            string newPass = txtRecoveryNewPass.Password;
            string confirmPass = txtRecoveryConfirmPass.Password;

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(newPass))
            {
                NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập Mã xác nhận và Mật khẩu mới!");
                return;
            }

            if (newPass != confirmPass)
            {
                NotificationManager.Show("LỖI", "Mật khẩu xác nhận không khớp!");
                return;
            }

            if (!(sender is Button btn)) return;

            try
            {
                btn.IsEnabled = false;
                btn.Content = "ĐANG ĐỔI MẬT KHẨU...";

                await Task.Delay(1500);

                var payload = new { username = username, otp = code, newPassword = newPass };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{API_BASE_URL}/reset-password", content);
                string responseString = await response.Content.ReadAsStringAsync();

                EnsureJsonResponse(responseString);

                var result = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response.IsSuccessStatusCode && result != null && result.Success)
                {
                    NotificationManager.Show("THÀNH CÔNG", "Đổi mật khẩu thành công! Hãy đăng nhập lại.");
                    ReturnToLogin();
                }
                else
                {
                    NotificationManager.Show("LỖI", result?.Message ?? "Mã OTP không chính xác hoặc đã hết hạn!");
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show("LỖI KẾT NỐI", ex.Message);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "ĐỔI MẬT KHẨU";
            }
        }
    }

    // === THÊM: Lớp dữ liệu hỗ trợ Ghi Nhớ Đăng Nhập ===
    public class UserSession
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class ApiResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public string? Username { get; set; }
        public string? Uuid { get; set; } 
    }
}
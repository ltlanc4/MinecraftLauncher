using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DotNetEnv;
using System.Reflection;

namespace MinecraftLauncher
{
    public partial class MainWindow : Window
    {
        private FrameworkElement _currentPanel;
        private static readonly HttpClient _httpClient = new HttpClient();

        // ĐỊA CHỈ BACKEND API
        private string API_BASE_URL;
        // TÊN TỆP LƯU TRỮ DATA
        private readonly string _appDataFolder;
        
        // CÁC FILE LƯU TRỮ CHUẨN MỚI
        private readonly string SESSION_FILE;
        private readonly string SETTINGS_FILE; // File gom chung cài đặt

        // BIẾN NGÔN NGỮ
        private bool _isEnglish = true; // Mặc định là Tiếng Anh
        private Dictionary<string, string> _langDict = new Dictionary<string, string>();

        public MainWindow()
        {
            // --- TỰ ĐỘNG DỌN RÁC SAU KHI CẬP NHẬT ---
            string oldExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName + ".old";
            if (File.Exists(oldExePath))
            {
                try { File.Delete(oldExePath); } catch { }
            }
            // ----------------------------------------
            InitializeComponent();

            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher");
            if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);

            // Gắn đường dẫn file chuẩn mới
            SESSION_FILE = Path.Combine(_appDataFolder, "session_data.json");
            SETTINGS_FILE = Path.Combine(_appDataFolder, "launcher_settings.json");

            // Lấy đường dẫn tuyệt đối đến thư mục chứa file .exe của Launcher
            string envPath = Path.Combine(_appDataFolder, ".env");

            var assembly = Assembly.GetExecutingAssembly();
            if (File.Exists(envPath))
            {
                DotNetEnv.Env.Load(envPath);
            }
            else
            {
                // Cú pháp tên file nhúng: [Tên_Namespace].[Tên_File]
                using (Stream stream = assembly.GetManifestResourceStream("MinecraftLauncher.default.env"))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string defaultContent = reader.ReadToEnd();
                            File.WriteAllText(envPath, defaultContent);
                        }
                    }
                    else
                    {
                        File.WriteAllText(envPath, "SERVER_API_IP=127.0.0.1\nSERVER_API_PORT=3000");
                    }
                }
                DotNetEnv.Env.Load(envPath);
            }

            string serverIP = Env.GetString("SERVER_API_IP");
            string serverPort = Env.GetString("SERVER_API_PORT");

            API_BASE_URL = $"http://{serverIP}:{serverPort}/auth";

            this.MouseLeftButtonDown += (s, e) => this.DragMove();
            _currentPanel = LoginPanel;

            // Lắng nghe sự kiện để tự động điền tài khoản khi mở Launcher
            this.Loaded += MainWindow_Loaded;

            // ================= ĐỌC NGÔN NGỮ TỪ FILE JSON CHUNG =================
            if (File.Exists(SETTINGS_FILE))
            {
                try
                {
                    string json = File.ReadAllText(SETTINGS_FILE);
                    var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                    _isEnglish = (settings != null && settings.Language == "EN");
                }
                catch { _isEnglish = true; } // Lỗi thì về mặc định
            }
            else
            {
                _isEnglish = true;
            }
            
            ApplyLanguage();
        }

        // ================= HỆ THỐNG ĐA NGÔN NGỮ =================
        private void btnLanguage_Click(object sender, RoutedEventArgs e)
        {
            string message = _isEnglish
                ? "The launcher needs to be restarted to apply the new language. Do you want to restart now?"
                : "Launcher cần khởi động lại để áp dụng ngôn ngữ mới. Bạn có muốn khởi động lại ngay bây giờ không?";
            string title = _isEnglish ? "RESTART REQUIRED" : "YÊU CẦU KHỞI ĐỘNG LẠI";

            // --- THÊM 2 DÒNG NÀY ĐỂ DỊCH NÚT BẤM ---
            string btnConfirmText = _isEnglish ? "CONFIRM" : "ĐỒNG Ý";
            string btnCancelText = _isEnglish ? "CANCEL" : "HỦY BỎ";

            // Truyền tên nút bấm vào hộp thoại
            bool isConfirm = NotificationManager.ShowConfirm(title, message, btnConfirmText, btnCancelText);

            if (isConfirm)
            {
                _isEnglish = !_isEnglish;
                
                // Đọc cài đặt JSON hiện tại (để không làm mất RAM và Path cũ)
                LauncherSettings currentSettings = new LauncherSettings();
                if (File.Exists(SETTINGS_FILE))
                {
                    try
                    {
                        string json = File.ReadAllText(SETTINGS_FILE);
                        currentSettings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
                    }
                    catch { }
                }

                // Cập nhật lại ngôn ngữ và lưu file
                currentSettings.Language = _isEnglish ? "EN" : "VI";
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(SETTINGS_FILE, JsonSerializer.Serialize(currentSettings, options));

                // Khởi động lại Launcher
                string currentExecutablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(currentExecutablePath);

                Application.Current.Shutdown();
            }
        }

        private void ApplyLanguage()
        {
            string langFile = _isEnglish ? "lang/en.pak" : "lang/vi.pak";
            try
            {
                if (File.Exists(langFile))
                {
                    string json = File.ReadAllText(langFile);
                    _langDict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
            }
            catch { }

            // Tự động map dữ liệu từ JSON vào toàn bộ Giao diện WPF
            foreach (var kvp in _langDict)
            {
                // Bỏ qua các key kịch bản động không map thẳng vào x:Name
                if (kvp.Key.StartsWith("msg_") || kvp.Key.StartsWith("btn_") || kvp.Key.StartsWith("lbl_")) continue;

                var element = this.FindName(kvp.Key);
                if (element is TextBlock tb) tb.Text = kvp.Value;
                else if (element is Button btn) btn.Content = kvp.Value;
                else if (element is CheckBox chk) chk.Content = kvp.Value;
            }

            // Đổi chữ hiển thị trên chính nút ngôn ngữ
            if (this.FindName("btnLanguage") is Button btnLang)
            {
                btnLang.Content = _isEnglish ? "EN-US" : "VI-VN";
            }

            // Xử lý giữ nguyên chữ cho các nút nếu chúng đang hiển thị
            var bLogin = this.FindName("btnLogin") as Button;
            if (bLogin != null && bLogin.IsEnabled) bLogin.Content = GetLang("btnLogin");

            var bReg = this.FindName("btnRegister") as Button;
            if (bReg != null && bReg.IsEnabled) bReg.Content = GetLang("btnRegister");

            var bSendCode = this.FindName("btnSendCode") as Button;
            if (bSendCode != null && bSendCode.IsEnabled) bSendCode.Content = GetLang("btnSendCode");

            var bConfirmReset = this.FindName("btnConfirmReset") as Button;
            if (bConfirmReset != null && bConfirmReset.IsEnabled) bConfirmReset.Content = GetLang("btnConfirmReset");

            if (this.FindName("lblLoadingText") is TextBlock lblLoad) lblLoad.Text = GetLang("lblLoadingText");
        }

        private string GetLang(string key)
        {
            if (_langDict != null && _langDict.ContainsKey(key)) return _langDict[key];
            return key;
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
                        txtLoginUsername.Text = session.Username;

                        string decodedPass = Encoding.UTF8.GetString(Convert.FromBase64String(session.Password));
                        txtLoginPassword.Password = decodedPass;

                        chkRememberMe.IsChecked = true;

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
                throw new Exception(_isEnglish ? "Server HTML error. Please check Node.js." : "Máy chủ không trả về JSON hợp lệ. Hãy kiểm tra xem Node.js đã bật chưa.");
            }
        }

        // ================= XỬ LÝ ĐĂNG NHẬP =================
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = txtLoginUsername.Text;
            string password = txtLoginPassword.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                NotificationManager.Show(_isEnglish ? "WARNING" : "CẢNH BÁO", _isEnglish ? "Please enter username and password!" : "Vui lòng nhập tài khoản và mật khẩu!");
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
                    NotificationManager.Show(_isEnglish ? "ERROR" : "LỖI", result?.Message ?? (_isEnglish ? "Incorrect username or password!" : "Tài khoản hoặc mật khẩu sai!"));
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                NotificationManager.Show(_isEnglish ? "CONNECTION ERROR" : "LỖI KẾT NỐI", ex.Message);
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
                NotificationManager.Show(_isEnglish ? "WARNING" : "CẢNH BÁO", _isEnglish ? "Please fill in all details!" : "Vui lòng nhập đầy đủ thông tin!");
                return;
            }

            if (pass != confirm)
            {
                NotificationManager.Show(_isEnglish ? "ERROR" : "LỖI", _isEnglish ? "Confirm password does not match!" : "Mật khẩu xác nhận không khớp!");
                return;
            }

            if (!(sender is Button btn)) return;

            try
            {
                btn.IsEnabled = false;
                btn.Content = _isEnglish ? "CREATING..." : "ĐANG TẠO...";

                await Task.Delay(1500);

                var payload = new { username = username, email = email, password = pass };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{API_BASE_URL}/register", content);
                string responseString = await response.Content.ReadAsStringAsync();

                EnsureJsonResponse(responseString);

                var result = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response.IsSuccessStatusCode && result != null && result.Success)
                {
                    NotificationManager.Show(_isEnglish ? "SUCCESS" : "THÀNH CÔNG", _isEnglish ? $"Account {username} created successfully!" : $"Tạo tài khoản {username} thành công!");
                    ReturnToLogin();
                }
                else
                {
                    NotificationManager.Show(_isEnglish ? "ERROR" : "LỖI", result?.Message ?? (_isEnglish ? "Username or Email already exists!" : "Tên tài khoản hoặc Email đã tồn tại!"));
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show(_isEnglish ? "CONNECTION ERROR" : "LỖI KẾT NỐI", ex.Message);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = GetLang("btnRegister");
            }
        }

        // ================= XỬ LÝ QUÊN MẬT KHẨU =================
        private async void SendRecoveryCode_Click(object sender, RoutedEventArgs e)
        {
            string username = txtRecoveryUsername.Text;
            string email = txtRecoveryEmail.Text;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
            {
                NotificationManager.Show(_isEnglish ? "WARNING" : "CẢNH BÁO", _isEnglish ? "Please enter Username and Email!" : "Vui lòng nhập Tên tài khoản và Email!");
                return;
            }

            if (!(sender is Button btn)) return;

            try
            {
                btn.IsEnabled = false;
                btn.Content = _isEnglish ? "SENDING CODE..." : "ĐANG GỬI MÃ...";

                await Task.Delay(1500);

                var payload = new { username = username, email = email };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{API_BASE_URL}/forgot-password", content);
                string responseString = await response.Content.ReadAsStringAsync();

                EnsureJsonResponse(responseString);

                var result = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response.IsSuccessStatusCode && result != null && result.Success)
                {
                    NotificationManager.Show(_isEnglish ? "OTP SENT" : "GỬI MÃ OTP", _isEnglish ? $"Verification code sent to email: {email}" : $"Mã xác nhận đã được gửi đến email: {email}");
                    AnimateTransition(pnlRecoveryStep1, pnlRecoveryStep2);
                }
                else
                {
                    NotificationManager.Show(_isEnglish ? "ERROR" : "LỖI", result?.Message ?? (_isEnglish ? "Invalid account details!" : "Thông tin tài khoản không hợp lệ!"));
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show(_isEnglish ? "CONNECTION ERROR" : "LỖI KẾT NỐI", ex.Message);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = GetLang("btnSendCode");
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
                NotificationManager.Show(_isEnglish ? "WARNING" : "CẢNH BÁO", _isEnglish ? "Please enter Verification Code and New Password!" : "Vui lòng nhập Mã xác nhận và Mật khẩu mới!");
                return;
            }

            if (newPass != confirmPass)
            {
                NotificationManager.Show(_isEnglish ? "ERROR" : "LỖI", _isEnglish ? "Confirm password does not match!" : "Mật khẩu xác nhận không khớp!");
                return;
            }

            if (!(sender is Button btn)) return;

            try
            {
                btn.IsEnabled = false;
                btn.Content = _isEnglish ? "CHANGING PASSWORD..." : "ĐANG ĐỔI MẬT KHẨU...";

                await Task.Delay(1500);

                var payload = new { username = username, otp = code, newPassword = newPass };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{API_BASE_URL}/reset-password", content);
                string responseString = await response.Content.ReadAsStringAsync();

                EnsureJsonResponse(responseString);

                var result = JsonSerializer.Deserialize<ApiResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response.IsSuccessStatusCode && result != null && result.Success)
                {
                    NotificationManager.Show(_isEnglish ? "SUCCESS" : "THÀNH CÔNG", _isEnglish ? "Password reset successfully! Please login." : "Đổi mật khẩu thành công! Hãy đăng nhập lại.");
                    ReturnToLogin();
                }
                else
                {
                    NotificationManager.Show(_isEnglish ? "ERROR" : "LỖI", result?.Message ?? (_isEnglish ? "OTP code is incorrect or expired!" : "Mã OTP không chính xác hoặc đã hết hạn!"));
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show(_isEnglish ? "CONNECTION ERROR" : "LỖI KẾT NỐI", ex.Message);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = GetLang("btnConfirmReset");
            }
        }
    }

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
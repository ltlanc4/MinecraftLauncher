using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;

namespace MinecraftLauncher
{
    public partial class HomeWindow : Window
    {
        private string _username; 
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // CẤU HÌNH API CỦA NODE.JS SERVER
        private readonly string API_SERVER_URL = "http://localhost:3000";
        private readonly string SESSION_FILE = "session_data.json"; 
        
        private ServerInfoResponse _serverManifest; 
        private bool _isInstalled = false; 

        // BIẾN LƯU TRỮ ĐƯỜNG DẪN CÀI ĐẶT
        private string _minecraftDirectory;
        private readonly string PATH_CONFIG_FILE = "launcher_path.txt"; 

        public HomeWindow(string username, string token, string uuid)
        {
            InitializeComponent();
            _username = username;
            
            // Cập nhật tên ở màn hình Trang Chủ
            txtUsername.Text = username.ToUpper();
            
            // Cập nhật tên ở màn hình Thông Tin Nhân Vật
            if (txtProfileDisplayUsername != null) txtProfileDisplayUsername.Text = username;
            if (txtProfileUsernameValue != null) txtProfileUsernameValue.Text = username;
            
            LoadMinecraftPath();
        }

        // ================= XỬ LÝ ĐƯỜNG DẪN CÀI ĐẶT =================
        private void LoadMinecraftPath()
        {
            string baseFolder;
            if (File.Exists(PATH_CONFIG_FILE))
            {
                baseFolder = File.ReadAllText(PATH_CONFIG_FILE).Trim();
            }
            else 
            {
                baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft");
            }

            _minecraftDirectory = EnsureMinecraftDirectory(baseFolder);
            txtInstallPath.Text = _minecraftDirectory;
        }

        private string EnsureMinecraftDirectory(string path)
        {
            if (!path.EndsWith("Minecraft", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(path, "Minecraft");
            }
            
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private void SaveMinecraftPath(string path)
        {
            _minecraftDirectory = EnsureMinecraftDirectory(path);
            File.WriteAllText(PATH_CONFIG_FILE, _minecraftDirectory);
            txtInstallPath.Text = _minecraftDirectory;
            
            CheckInstallationStatus(); 
        }

        private void ChangePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Chọn thư mục chứa game (Thư mục 'Minecraft' sẽ được tự động tạo)";
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SaveMinecraftPath(dialog.SelectedPath);
            }
        }

        // ================= TẢI DỮ LIỆU TỪ SERVER =================
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadServerInfoFromManifest();
        }

        private async Task LoadServerInfoFromManifest()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{API_SERVER_URL}/auth/server-info");
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    
                    var jsonOptions = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    };
                    
                    _serverManifest = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, jsonOptions);

                    if (_serverManifest != null && _serverManifest.Success)
                    {
                        txtGameVersion.Text = $"{_serverManifest.Loader} {_serverManifest.Version}";
                        txtTotalMods.Text = $"Đã kiểm tra đồng bộ: {_serverManifest.TotalMods} Mods hoạt động.";
                        icModsList.ItemsSource = _serverManifest.Mods;
                        
                        CheckInstallationStatus();
                        return;
                    }
                }
                txtGameVersion.Text = "Cấu hình Offline";
                txtTotalMods.Text = "Không thể tải danh sách Mod.";
            }
            catch (Exception ex)
            {
                txtGameVersion.Text = "Lỗi hệ thống";
                txtTotalMods.Text = $"Chi tiết lỗi: {ex.Message}";
            }
        }

        private string GetTargetVersionName()
        {
            if (_serverManifest == null || string.IsNullOrEmpty(_serverManifest.Version)) return "";
            
            if (_serverManifest.Loader != null && _serverManifest.Loader.ToLower() == "fabric" && !string.IsNullOrEmpty(_serverManifest.Loader_Version))
            {
                return $"fabric-loader-{_serverManifest.Loader_Version}-{_serverManifest.Version}";
            }
            return _serverManifest.Version; 
        }

        private void CheckInstallationStatus()
        {
            if (_serverManifest == null || string.IsNullOrEmpty(_serverManifest.Version)) return;

            var path = new MinecraftPath(_minecraftDirectory);
            string targetVersion = GetTargetVersionName();
            string versionFolder = Path.Combine(path.Versions, targetVersion);
            string jsonFile = Path.Combine(versionFolder, targetVersion + ".json");
            string modsDir = Path.Combine(path.BasePath, "mods");

            bool isGameInstalled = File.Exists(jsonFile);
            
            bool areModsInstalled = true;
            if (_serverManifest.Mods != null && _serverManifest.Mods.Count > 0)
            {
                if (!Directory.Exists(modsDir)) 
                {
                    areModsInstalled = false;
                } 
                else 
                {
                    var localFiles = Directory.GetFiles(modsDir).Select(Path.GetFileName).ToList();
                    var missingMods = _serverManifest.Mods.Except(localFiles).ToList();
                    if (missingMods.Count > 0) areModsInstalled = false;
                }
            }

            _isInstalled = isGameInstalled && areModsInstalled;
            
            Dispatcher.Invoke(() => {
                btnPlay.Content = _isInstalled ? "KHỞI ĐỘNG" : "CÀI ĐẶT";
            });
        }

        // ================= CƠ CHẾ ĐỒNG BỘ MODS (DELTA SYNC) =================
        private async Task SyncModsAsync(string modsDirectory)
        {
            if (_serverManifest == null || _serverManifest.Mods == null || _serverManifest.Mods.Count == 0)
                return;

            if (!Directory.Exists(modsDirectory))
                Directory.CreateDirectory(modsDirectory);

            var localFiles = Directory.GetFiles(modsDirectory).Select(Path.GetFileName).ToList();
            var serverFiles = _serverManifest.Mods;

            var filesToDelete = localFiles.Except(serverFiles).ToList();
            foreach (var file in filesToDelete)
            {
                try { File.Delete(Path.Combine(modsDirectory, file)); } 
                catch { }
            }

            var filesToDownload = serverFiles.Except(localFiles).ToList();
            if (filesToDownload.Count > 0)
            {
                Dispatcher.Invoke(() => {
                    pbDownload.Maximum = filesToDownload.Count;
                    pbDownload.Value = 0;
                });

                for (int i = 0; i < filesToDownload.Count; i++)
                {
                    string modFile = filesToDownload[i];
                    Dispatcher.Invoke(() => {
                        txtDownloadStatus.Text = $"Đang tải Mod: {modFile}";
                        txtDownloadDetail.Text = $"{i} / {filesToDownload.Count} Tệp";
                        txtDownloadPercentage.Text = $"{((double)i / filesToDownload.Count * 100):F0}%";
                    });

                    string fileUrl = $"{API_SERVER_URL}/mods/{modFile}";
                    string savePath = Path.Combine(modsDirectory, modFile);

                    try
                    {
                        byte[] fileBytes = await _httpClient.GetByteArrayAsync(fileUrl);
                        await File.WriteAllBytesAsync(savePath, fileBytes);
                    }
                    catch (Exception)
                    {
                        // Bỏ qua hiển thị thông báo lỗi từng mod để tránh gián đoạn
                    }

                    Dispatcher.Invoke(() => { pbDownload.Value = i + 1; });
                }
            }
        }

        // ================= XỬ LÝ KHỞI ĐỘNG TRÒ CHƠI =================
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serverManifest == null)
            {
                MessageBox.Show("Chưa lấy được cấu hình phiên bản từ máy chủ!");
                return;
            }

            btnPlay.IsEnabled = false;
            DownloadProgressContainer.Visibility = Visibility.Visible;
            pbDownload.Value = 0;

            try
            {
                var path = new MinecraftPath(_minecraftDirectory); 
                var launcher = new CMLauncher(path);

                launcher.FileChanged += (fileEvent) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtDownloadStatus.Text = $"[{fileEvent.FileKind}] {fileEvent.FileName}";
                        txtDownloadDetail.Text = $"{fileEvent.ProgressedFileCount} / {fileEvent.TotalFileCount}";
                        pbDownload.Maximum = fileEvent.TotalFileCount;
                        pbDownload.Value = fileEvent.ProgressedFileCount;
                        double pct = fileEvent.TotalFileCount > 0 ? ((double)fileEvent.ProgressedFileCount / fileEvent.TotalFileCount) * 100 : 0;
                        txtDownloadPercentage.Text = $"{pct:F0}%";
                    });
                };

                string targetVersion = GetTargetVersionName();

                if (!_isInstalled)
                {
                    if (targetVersion.StartsWith("fabric-loader"))
                    {
                        string versionDir = Path.Combine(path.Versions, targetVersion);
                        string jsonPath = Path.Combine(versionDir, targetVersion + ".json");

                        if (!File.Exists(jsonPath))
                        {
                            Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang lấy dữ liệu Fabric từ máy chủ chính thức...");
                            if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);
                            
                            string fabricApiUrl = $"https://meta.fabricmc.net/v2/versions/loader/{_serverManifest.Version}/{_serverManifest.Loader_Version}/profile/json";
                            
                            try 
                            {
                                byte[] jsonBytes = await _httpClient.GetByteArrayAsync(fabricApiUrl);
                                await File.WriteAllBytesAsync(jsonPath, jsonBytes);
                            }
                            catch (Exception ex)
                            {
                                throw new Exception($"Không thể lấy cấu hình Fabric: {ex.Message}");
                            }
                        }
                    }

                    Dispatcher.Invoke(() => {
                        btnPlay.Content = "ĐANG TẢI...";
                        txtDownloadStatus.Text = "Đang cài đặt thư viện Minecraft Core và Fabric...";
                    });
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        btnPlay.Content = "ĐANG KIỂM TRA...";
                        txtDownloadStatus.Text = "Đang kiểm tra cập nhật tài nguyên...";
                    });
                }

                // KẾT NỐI TRỰC TIẾP BẰNG IP SỐ VÀ PORT GỐC 
                var launchOption = new MLaunchOption
                {
                    Session = MSession.GetOfflineSession(_username), 
                    MaximumRamMb = 4096, 
                    ServerIp = string.IsNullOrEmpty(_serverManifest.Server_Ip) ? "127.0.0.1" : _serverManifest.Server_Ip,
                    ServerPort = _serverManifest.Server_Port > 0 ? _serverManifest.Server_Port : 25565
                };

                var process = await launcher.CreateProcessAsync(targetVersion, launchOption);

                Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang đồng bộ Mods từ Máy chủ...");
                string modsDir = Path.Combine(path.BasePath, "mods");
                await SyncModsAsync(modsDir);

                Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang khởi động trò chơi...");
                await Task.Delay(500);

                process.EnableRaisingEvents = true; 
                process.Exited += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnPlay.IsEnabled = true;
                        btnPlay.Content = "KHỞI ĐỘNG";
                        this.WindowState = WindowState.Normal;
                    });
                };
                
                process.Start(); 

                DownloadProgressContainer.Visibility = Visibility.Collapsed;
                _isInstalled = true; 

                btnPlay.Content = "ĐANG CHƠI";
                btnPlay.IsEnabled = false; 
                
                this.WindowState = WindowState.Minimized; 
            }
            catch (Exception ex)
            {
                MessageBox.Show("LỖI HỆ THỐNG: " + ex.Message);
                DownloadProgressContainer.Visibility = Visibility.Collapsed;
                btnPlay.IsEnabled = true;
                btnPlay.Content = _isInstalled ? "KHỞI ĐỘNG" : "CÀI ĐẶT";
            }
        }


        // ===============================================================
        // CÁC HÀM XỬ LÝ HỒ SƠ NHÂN VẬT (TAB PROFILE)
        // ===============================================================

        private async void btnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            string oldPass = txtOldPassword.Password;
            string newPass = txtNewPassword.Password;

            if (string.IsNullOrEmpty(oldPass) || string.IsNullOrEmpty(newPass))
            {
                MessageBox.Show("Vui lòng nhập đủ mật khẩu cũ và mới!", "CẢNH BÁO", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var payload = new { username = _username, oldPassword = oldPass, newPassword = newPass };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/change-password", content);
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result != null && result.Success)
                {
                    MessageBox.Show("Đổi mật khẩu thành công!", "THÀNH CÔNG", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtOldPassword.Password = "";
                    txtNewPassword.Password = "";
                    
                    if (File.Exists(SESSION_FILE))
                    {
                        try 
                        {
                            var sessionJson = File.ReadAllText(SESSION_FILE);
                            var sessionDict = JsonSerializer.Deserialize<Dictionary<string, string>>(sessionJson);
                            if (sessionDict != null && sessionDict.ContainsKey("Username"))
                            {
                                sessionDict["Password"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(newPass));
                                File.WriteAllText(SESSION_FILE, JsonSerializer.Serialize(sessionDict));
                            }
                        }
                        catch { }
                    }
                }
                else 
                {
                    MessageBox.Show(result?.Message ?? "Mật khẩu cũ không chính xác!", "LỖI", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) 
            { 
                MessageBox.Show(ex.Message, "LỖI KẾT NỐI", MessageBoxButton.OK, MessageBoxImage.Error); 
            }
        }

        private async void btnSendEmailOtp_Click(object sender, RoutedEventArgs e)
        {
            string newEmail = txtNewEmail.Text;
            if (string.IsNullOrEmpty(newEmail)) 
            {
                MessageBox.Show("Vui lòng nhập Email mới!", "CẢNH BÁO", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var payload = new { username = _username, newEmail = newEmail };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/request-email-change", content);
                string responseString = await response.Content.ReadAsStringAsync();

                // LỚP BẢO VỆ: NẾU SERVER TRẢ VỀ HTML BÁO LỖI THAY VÌ JSON
                if (responseString.Trim().StartsWith("<"))
                {
                    MessageBox.Show("Máy chủ Node.js đang bị lỗi (Crash) và trả về trang HTML thay vì JSON.\n\nVui lòng mở cửa sổ CMD/Terminal chạy Node.js lên để xem chi tiết dòng lỗi màu đỏ!", "LỖI BACKEND NODE.JS", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result != null && result.Success)
                {
                    MessageBox.Show("Đã gửi mã OTP đến email mới của bạn!", "THÀNH CÔNG", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (pnlOtpVerify != null) pnlOtpVerify.Visibility = Visibility.Visible; 
                }
                else 
                {
                    MessageBox.Show(result?.Message ?? "Lỗi gửi email!", "LỖI", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) 
            { 
                MessageBox.Show(ex.Message, "LỖI KẾT NỐI", MessageBoxButton.OK, MessageBoxImage.Error); 
            }
        }

        private async void btnConfirmEmailChange_Click(object sender, RoutedEventArgs e)
        {
            string newEmail = txtNewEmail.Text;
            string otp = txtEmailOtp.Text;

            if (string.IsNullOrEmpty(newEmail) || string.IsNullOrEmpty(otp))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ Email mới và OTP!", "CẢNH BÁO", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var payload = new { username = _username, newEmail = newEmail, otp = otp };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/change-email", content);
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result != null && result.Success)
                {
                    MessageBox.Show("Cập nhật Email khôi phục thành công!", "THÀNH CÔNG", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtNewEmail.Text = "";
                    txtEmailOtp.Text = "";
                    if (pnlOtpVerify != null) pnlOtpVerify.Visibility = Visibility.Collapsed; 
                }
                else 
                {
                    MessageBox.Show(result?.Message ?? "Mã OTP Sai!", "LỖI", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) 
            { 
                MessageBox.Show(ex.Message, "LỖI KẾT NỐI", MessageBoxButton.OK, MessageBoxImage.Error); 
            }
        }

        // ===============================================================
        // SỰ KIỆN CHUYỂN TAB CÓ ANIMATION MƯỢT MÀ
        // ===============================================================
        
        private void PlayTransitionAnimation(FrameworkElement targetPanel, params FrameworkElement[] panelsToHide)
        {
            if (targetPanel == null) return;

            foreach (var panel in panelsToHide)
            {
                if (panel != null) panel.Visibility = Visibility.Collapsed;
            }

            targetPanel.Visibility = Visibility.Visible;
            targetPanel.Opacity = 0;

            TranslateTransform trans = new TranslateTransform(0, 15);
            targetPanel.RenderTransform = trans;

            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            DoubleAnimation slideUp = new DoubleAnimation(15, 0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            targetPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            trans.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void TabHome_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewHome != null && ViewProfile != null)
                PlayTransitionAnimation(ViewHome, ViewProfile);
        }

        private void TabProfile_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewHome != null && ViewProfile != null)
                PlayTransitionAnimation(ViewProfile, ViewHome);
        }

        private void SubTabInfo_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlProfileInfo == null) return;
            PlayTransitionAnimation(pnlProfileInfo, pnlProfileEmail, pnlProfilePassword);
        }

        private void SubTabEmail_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlProfileEmail == null) return;
            PlayTransitionAnimation(pnlProfileEmail, pnlProfileInfo, pnlProfilePassword);
        }

        private void SubTabPassword_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlProfilePassword == null) return;
            PlayTransitionAnimation(pnlProfilePassword, pnlProfileInfo, pnlProfileEmail);
        }

        // ===============================================================
        // CÁC SỰ KIỆN ĐIỀU KHIỂN CỬA SỔ CHUNG
        // ===============================================================

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { this.DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { Application.Current.Shutdown(); }
        
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(SESSION_FILE)) 
            {
                File.Delete(SESSION_FILE);
            }

            this.IsHitTestVisible = false;
            var fadeOutHome = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fadeOutHome.Completed += (s, ev) => {
                MainWindow loginWindow = new MainWindow();
                loginWindow.Opacity = 0; 
                loginWindow.Show();
                var fadeInLogin = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
                loginWindow.BeginAnimation(Window.OpacityProperty, fadeInLogin);
                this.Close();
            };
            this.BeginAnimation(Window.OpacityProperty, fadeOutHome);
        }
    }

    // ===============================================================
    // LỚP MODEL NHẬN DỮ LIỆU TỪ BACKEND
    // ===============================================================
    public class ServerInfoResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; } 
        public string? Version { get; set; }
        public string? Loader { get; set; }
        public string? Loader_Version { get; set; } 
        public string? Server_Ip { get; set; }  
        public int Server_Port { get; set; }     
        public int TotalMods { get; set; }
        public List<string>? Mods { get; set; }
    }
}
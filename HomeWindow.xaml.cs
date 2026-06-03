using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CmlLib.Core;
using CmlLib.Core.Auth;
using System.Windows.Threading;

namespace MinecraftLauncher
{
    public partial class HomeWindow : Window
    {
        private string _username; 
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string API_SERVER_URL = "http://localhost:3000";
        private ServerInfoResponse _serverManifest; 
        private bool _isInstalled = false; 

        private string _minecraftDirectory;
        private readonly string PATH_CONFIG_FILE = "launcher_path.txt"; 

        public HomeWindow(string username, string token, string uuid)
        {
            InitializeComponent();
            _username = username;
            txtUsername.Text = username.ToUpper();
            
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
                    _serverManifest = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
            catch (Exception)
            {
                txtGameVersion.Text = "Lỗi kết nối";
                txtTotalMods.Text = "Mất kết nối máy chủ API.";
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

        // ================= CƠ CHẾ ĐỒNG BỘ MODS =================
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
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => NotificationManager.Show("LỖI TẢI MOD", $"Không thể tải {modFile}: {ex.Message}"));
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
                NotificationManager.Show("LỖI KẾT NỐI", "Chưa lấy được cấu hình phiên bản từ máy chủ!");
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
                    // TỰ ĐỘNG LẤY FILE JSON TỪ FABRIC META API
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
                    
                    var process = await launcher.CreateProcessAsync(targetVersion, new MLaunchOption
                    {
                        Session = MSession.GetOfflineSession(_username), 
                        MaximumRamMb = 4096 
                    });

                    Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang đồng bộ Mods từ Máy chủ...");
                    string modsDir = Path.Combine(path.BasePath, "mods");
                    await SyncModsAsync(modsDir);

                    Dispatcher.Invoke(() => txtDownloadStatus.Text = "Cài đặt hoàn tất!");
                    await Task.Delay(1000);

                    _isInstalled = true;
                    DownloadProgressContainer.Visibility = Visibility.Collapsed;
                    btnPlay.IsEnabled = true;
                    btnPlay.Content = "KHỞI ĐỘNG";
                    NotificationManager.Show("CÀI ĐẶT XONG", "Trò chơi đã sẵn sàng. Bạn có thể nhấn Khởi Động!");
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        btnPlay.Content = "ĐANG VÀO GAME...";
                        txtDownloadStatus.Text = "Đang kiểm tra cập nhật tài nguyên...";
                    });
                    
                    var process = await launcher.CreateProcessAsync(targetVersion, new MLaunchOption
                    {
                        Session = MSession.GetOfflineSession(_username), 
                        MaximumRamMb = 4096 
                    });

                    string modsDir = Path.Combine(path.BasePath, "mods");
                    await SyncModsAsync(modsDir);

                    Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang khởi động tiến trình trò chơi...");
                    await Task.Delay(500);
                    
                    process.Start(); 

                    DownloadProgressContainer.Visibility = Visibility.Collapsed;
                    btnPlay.IsEnabled = true;
                    btnPlay.Content = "ĐANG CHƠI";
                    NotificationManager.Show("VÀO GAME", "Minecraft đang khởi động. Launcher sẽ tự thu nhỏ.");
                    
                    this.WindowState = WindowState.Minimized; 
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show("LỖI HỆ THỐNG", ex.Message);
                DownloadProgressContainer.Visibility = Visibility.Collapsed;
                btnPlay.IsEnabled = true;
                btnPlay.Content = _isInstalled ? "KHỞI ĐỘNG" : "CÀI ĐẶT";
            }
        }

        // ================= CÁC SỰ KIỆN GIAO DIỆN KHÁC =================
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { this.DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { Application.Current.Shutdown(); }
        
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
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

    public class ServerInfoResponse
    {
        public bool Success { get; set; }
        public string? Version { get; set; }
        public string? Loader { get; set; }
        public string? Loader_Version { get; set; } 
        public int TotalMods { get; set; }
        public List<string>? Mods { get; set; }
    }
}

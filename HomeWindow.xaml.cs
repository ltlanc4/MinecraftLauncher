using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using Microsoft.Win32;
using System.Windows.Media.Imaging;

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

        // === RADAR BẮT SỰ KIỆN TỰ ĐỘNG CẬP NHẬT MOD ===
        private DispatcherTimer _autoSyncTimer;
        
        // THÊM DÒNG NÀY: RADAR BẮT SỰ KIỆN FILE BỊ XÓA LOCAL
        private FileSystemWatcher _modsWatcher;

        // LƯU TRỮ ẢNH SKIN ĐƯỢC CHỌN (DẠNG BASE64)
        private string _selectedSkinBase64 = "";

        public HomeWindow(string username, string token, string uuid)
        {
            InitializeComponent();

            // FIX LỖI NOTIFICATION: Chuyển quyền "Cửa sổ chính" cho HomeWindow
            // Để NotificationManager biết neo thông báo vào đâu
            Application.Current.MainWindow = this;

            _username = username;

            // Cập nhật tên ở màn hình Trang Chủ
            txtUsername.Text = username.ToUpper();

            // Cập nhật tên ở màn hình Thông Tin Nhân Vật
            if (txtProfileDisplayUsername != null) txtProfileDisplayUsername.Text = username;
            if (txtProfileUsernameValue != null) txtProfileUsernameValue.Text = username;

            LoadMinecraftPath();

            // THIẾT LẬP VÀ CHẠY RADAR (BẮT SỰ KIỆN TỰ ĐỘNG MỖI 3 GIÂY)
            _autoSyncTimer = new DispatcherTimer();
            _autoSyncTimer.Interval = TimeSpan.FromSeconds(3);
            _autoSyncTimer.Tick += async (s, ev) => await AutoCheckServerUpdates();
            _autoSyncTimer.Start();
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
            await LoadUserSkinAsync(); 
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
                        Dispatcher.Invoke(() =>
                        {
                            txtGameVersion.Text = $"{_serverManifest.Loader} {_serverManifest.Version}";
                            txtTotalMods.Text = $"Đã kiểm tra đồng bộ: {_serverManifest.TotalMods} Mods hoạt động.";
                            icModsList.ItemsSource = _serverManifest.Mods;
                        });
                        CheckInstallationStatus();
                        return;
                    }
                }
                Dispatcher.Invoke(() =>
                {
                    txtGameVersion.Text = "Cấu hình Offline";
                    txtTotalMods.Text = "Không thể tải danh sách Mod.";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtGameVersion.Text = "Lỗi hệ thống";
                    txtTotalMods.Text = $"Chi tiết lỗi: {ex.Message}";
                });
            }
        }

        // ================= BỘ LẮNG NGHE TỰ ĐỘNG (RADAR) =================
        private async Task AutoCheckServerUpdates()
        {
            if (!btnPlay.IsEnabled) return;

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

                    var newManifest = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, jsonOptions);

                    if (newManifest != null && newManifest.Success)
                    {
                        bool isChanged = false;

                        if (_serverManifest == null || _serverManifest.TotalMods != newManifest.TotalMods)
                        {
                            isChanged = true;
                        }
                        else if (_serverManifest.Mods != null && newManifest.Mods != null)
                        {
                            var diff1 = newManifest.Mods.Except(_serverManifest.Mods).ToList();
                            var diff2 = _serverManifest.Mods.Except(newManifest.Mods).ToList();
                            if (diff1.Count > 0 || diff2.Count > 0) isChanged = true;
                        }

                        if (isChanged)
                        {
                            _serverManifest = newManifest;
                            Dispatcher.Invoke(() =>
                            {
                                txtGameVersion.Text = $"{_serverManifest.Loader} {_serverManifest.Version}";
                                txtTotalMods.Text = $"Đã kiểm tra đồng bộ: {_serverManifest.TotalMods} Mods hoạt động.";
                                icModsList.ItemsSource = _serverManifest.Mods;
                            });
                            CheckInstallationStatus();
                        }
                    }
                }
            }
            catch { }
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

            // === 1. THIẾT LẬP RADAR THEO DÕI THƯ MỤC MODS TRÊN Ổ CỨNG ===
            if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
            
            if (_modsWatcher == null || _modsWatcher.Path != modsDir)
            {
                if (_modsWatcher != null)
                {
                    _modsWatcher.EnableRaisingEvents = false;
                    _modsWatcher.Dispose();
                }
                
                _modsWatcher = new FileSystemWatcher(modsDir);
                _modsWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
                
                FileSystemEventHandler onModChanged = (s, e) => {
                    Dispatcher.Invoke(() => {
                        // Cực kỳ quan trọng: Bỏ qua nếu Launcher đang tải Mod hoặc đang trong Game
                        if (btnPlay == null || btnPlay.IsEnabled == false) return; 
                        CheckInstallationStatus();
                    });
                };

                _modsWatcher.Deleted += onModChanged;
                _modsWatcher.Created += onModChanged;
                _modsWatcher.Renamed += new RenamedEventHandler(onModChanged);
                
                _modsWatcher.EnableRaisingEvents = true;
            }
            // ==============================================================

            bool isGameInstalled = File.Exists(jsonFile);
            bool areModsInstalled = true;
            
            var displayList = new List<ModStatusItem>();

            if (_serverManifest.Mods != null && _serverManifest.Mods.Count > 0)
            {
                var localFiles = Directory.GetFiles(modsDir).Select(Path.GetFileName).ToList();
                var missingMods = _serverManifest.Mods.Except(localFiles).ToList();
                var extraMods = localFiles.Except(_serverManifest.Mods).ToList(); 
                
                if (missingMods.Count > 0 || extraMods.Count > 0) areModsInstalled = false;

                foreach (var m in _serverManifest.Mods) 
                {
                    displayList.Add(new ModStatusItem { FileName = m, IsInstalled = localFiles.Contains(m) });
                }
            }

            _isInstalled = isGameInstalled && areModsInstalled;

            Dispatcher.Invoke(() =>
            {
                if (icModsList != null) icModsList.ItemsSource = displayList;

                // Nếu Game đang bật thì không đổi chữ của Nút Play
                if (btnPlay.Content != null && btnPlay.Content.ToString() == "ĐANG CHƠI") return;

                if (_isInstalled)
                {
                    btnPlay.Content = "KHỞI ĐỘNG";
                    if (txtVersionLabel != null) txtVersionLabel.Text = "Phiên bản hiện tại: ";
                }
                else if (isGameInstalled && !areModsInstalled)
                {
                    btnPlay.Content = "CẬP NHẬT";
                    if (txtVersionLabel != null) txtVersionLabel.Text = "Phiên bản hiện tại: ";
                }
                else
                {
                    btnPlay.Content = "CÀI ĐẶT";
                    if (txtVersionLabel != null) txtVersionLabel.Text = "Phiên bản chuẩn bị tải: ";
                }
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
                Dispatcher.Invoke(() =>
                {
                    pbDownload.Maximum = filesToDownload.Count;
                    pbDownload.Value = 0;
                });

                for (int i = 0; i < filesToDownload.Count; i++)
                {
                    string modFile = filesToDownload[i];
                    Dispatcher.Invoke(() =>
                    {
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
                    catch (Exception) { }

                    Dispatcher.Invoke(() => { pbDownload.Value = i + 1; });
                }
            }
        }

        // ================= XỬ LÝ KHỞI ĐỘNG TRÒ CHƠI =================
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serverManifest == null)
            {
                Dispatcher.Invoke(() => NotificationManager.Show("LỖI KẾT NỐI", "Chưa lấy được cấu hình phiên bản từ máy chủ!"));
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

                    Dispatcher.Invoke(() =>
                    {
                        btnPlay.Content = "ĐANG TẢI...";
                        txtDownloadStatus.Text = "Đang cài đặt thư viện Minecraft Core và Fabric...";
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnPlay.Content = "ĐANG KIỂM TRA...";
                        txtDownloadStatus.Text = "Đang kiểm tra cập nhật tài nguyên...";
                    });
                }

                // ====================================================================
                // TỰ ĐỘNG NHẬN DIỆN CHẾ ĐỘ MÀN HÌNH TỪ CÀI ĐẶT TRONG GAME
                // ====================================================================
                bool isFullScreen = false;
                string optionsFile = Path.Combine(path.BasePath, "options.txt");
                
                if (File.Exists(optionsFile))
                {
                    try
                    {
                        // Đọc file cài đặt của Minecraft để xem lần trước người chơi chọn gì
                        var lines = File.ReadAllLines(optionsFile);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("fullscreen:"))
                            {
                                isFullScreen = line.Split(':')[1].Trim().ToLower() == "true";
                                break;
                            }
                        }
                    }
                    catch { }
                }

                var launchOption = new MLaunchOption
                {
                    Session = MSession.GetOfflineSession(_username),
                    MaximumRamMb = 4096,
                    ServerIp = string.IsNullOrEmpty(_serverManifest.Server_Ip) ? "127.0.0.1" : _serverManifest.Server_Ip,
                    ServerPort = _serverManifest.Server_Port > 0 ? _serverManifest.Server_Port : 25565
                };

                // ÁP DỤNG TRẠNG THÁI MÀN HÌNH
                if (isFullScreen)
                {
                    launchOption.FullScreen = true; // Chạy Fullscreen không viền chuẩn của game
                }
                else
                {
                    // Chạy ở chế độ Cửa sổ Phóng to (Maximized)
                    launchOption.ScreenWidth = (int)SystemParameters.WorkArea.Width;
                    launchOption.ScreenHeight = (int)SystemParameters.WorkArea.Height;
                }
                // ====================================================================

                var process = await launcher.CreateProcessAsync(targetVersion, launchOption);

                Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang đồng bộ Mods từ Máy chủ...");
                string modsDir = Path.Combine(path.BasePath, "mods");
                await SyncModsAsync(modsDir);

                Dispatcher.Invoke(() => CheckInstallationStatus());
                // ====================================================================
                // TỰ ĐỘNG CẤU HÌNH CUSTOM SKIN LOADER
                // ====================================================================
                Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang cấu hình máy chủ Skin...");
                try
                {
                    string cslFolder = Path.Combine(path.BasePath, "CustomSkinLoader");
                    if (!Directory.Exists(cslFolder)) Directory.CreateDirectory(cslFolder);

                    string cslConfigPath = Path.Combine(cslFolder, "CustomSkinLoader.json");

                    string cslConfigContent = @"
                    {
                      ""version"": ""14.12"",
                      ""loadlist"": [
                        {
                          ""name"": ""OtonashiRei_LocalServer"",
                          ""type"": ""Legacy"",
                          ""skin"": """ + API_SERVER_URL + @"/skins/{USERNAME}.png"",
                          ""checkPNG"": true
                        },
                        {
                          ""name"": ""Mojang"",
                          ""type"": ""MojangAPI""
                        }
                      ],
                      ""enableDynamicSkull"": true,
                      ""enableTransparentSkin"": true,
                      ""ignoreHttpsCertificate"": false
                    }";

                    File.WriteAllText(cslConfigPath, cslConfigContent);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Lỗi ghi file CustomSkinLoader: " + ex.Message);
                }
                // ====================================================================

                Dispatcher.Invoke(() => txtDownloadStatus.Text = "Đang khởi động trò chơi...");
                await Task.Delay(500);

                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnPlay.IsEnabled = true;
                        
                        // FIX LỖI KẸT CHỮ: Đổi tạm chữ khác để "đánh lừa" chốt chặn bảo vệ
                        btnPlay.Content = "ĐANG KIỂM TRA..."; 
                        
                        // Lúc này chữ không còn là "ĐANG CHƠI" nữa nên hàm quét sẽ hoạt động 100%
                        CheckInstallationStatus(); 
                        
                        this.WindowState = WindowState.Normal;
                    });
                };

                process.Start();

                Dispatcher.Invoke(() =>
                {
                    DownloadProgressContainer.Visibility = Visibility.Collapsed;
                    _isInstalled = true; // Đánh dấu là đã cài đặt xong
                    
                    // THÊM DÒNG NÀY: Cập nhật chữ thành "Phiên bản hiện tại" ngay sau khi tải xong
                    if (txtVersionLabel != null) 
                    {
                        txtVersionLabel.Text = "Phiên bản hiện tại: ";
                    }
                    
                    btnPlay.Content = "ĐANG CHƠI";
                    btnPlay.IsEnabled = false;
                    NotificationManager.Show("VÀO GAME", "Đang kết nối trực tiếp siêu tốc tới máy chủ...");
                    this.WindowState = WindowState.Minimized;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    NotificationManager.Show("LỖI HỆ THỐNG", ex.Message);
                    DownloadProgressContainer.Visibility = Visibility.Collapsed;
                    btnPlay.IsEnabled = true;
                    
                    // Gọi lại hàm kiểm tra để tự động set chữ đúng trạng thái
                    CheckInstallationStatus(); 
                });
            }
        }

        // ===============================================================
        // QUẢN LÝ SKIN NHÂN VẬT (MỚI)
        // ===============================================================

        // Hàm phụ trợ: Cắt và xếp chồng 2 lớp khuôn mặt (Base + Hat Overlay)
        private ImageSource GetSkinFace(BitmapSource skinBitmap)
        {
            // 1. Cắt lớp khuôn mặt cơ bản (Nằm ở X:8, Y:8)
            var baseHead = new CroppedBitmap(skinBitmap, new Int32Rect(8, 8, 8, 8));

            // 2. Cắt lớp Mặt nạ / Mũ / Tóc (Nằm ở X:40, Y:8)
            var hatLayer = new CroppedBitmap(skinBitmap, new Int32Rect(40, 8, 8, 8));

            // 3. Xếp chồng lớp Mặt Nạ lên trên lớp Cơ Bản
            var drawingGroup = new DrawingGroup();
            
            // Giữ cho viền ảnh Pixel Art luôn sắc nét, không bị mờ
            RenderOptions.SetBitmapScalingMode(drawingGroup, BitmapScalingMode.NearestNeighbor);

            using (var ctx = drawingGroup.Open())
            {
                ctx.DrawImage(baseHead, new Rect(0, 0, 8, 8));
                ctx.DrawImage(hatLayer, new Rect(0, 0, 8, 8)); 
            }

            return new DrawingImage(drawingGroup);
        }

        private async Task LoadUserSkinAsync()
        {
            try
            {
                string skinUrl = $"{API_SERVER_URL}/skins/{_username}.png?t={DateTime.Now.Ticks}";
                var skinData = await _httpClient.GetByteArrayAsync(skinUrl);
                
                using (var ms = new MemoryStream(skinData))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();

                    if (bitmap.PixelWidth >= 64 && bitmap.PixelHeight >= 32)
                    {
                        var faceImage = GetSkinFace(bitmap);

                        Dispatcher.Invoke(() => {
                            if (imgProfileAvatar != null) imgProfileAvatar.Source = faceImage;
                            if (txtDefaultAvatar != null) txtDefaultAvatar.Visibility = Visibility.Collapsed;
                        });
                    }
                }
            }
            catch
            {
                Dispatcher.Invoke(() => {
                    if (imgProfileAvatar != null) imgProfileAvatar.Source = null;
                    if (txtDefaultAvatar != null) txtDefaultAvatar.Visibility = Visibility.Visible;
                });
            }
        }

        private void btnSelectSkin_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "PNG Image (*.png)|*.png",
                Title = "Chọn file Skin Minecraft"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(openFileDialog.FileName));
                    
                    if (bitmap.PixelWidth != 64 || (bitmap.PixelHeight != 64 && bitmap.PixelHeight != 32))
                    {
                        NotificationManager.Show("LỖI SKIN", "File skin phải có kích thước 64x64 hoặc 64x32!");
                        return;
                    }

                    if (imgSkinPreview != null) imgSkinPreview.Source = bitmap;
                    
                    byte[] imageBytes = File.ReadAllBytes(openFileDialog.FileName);
                    _selectedSkinBase64 = Convert.ToBase64String(imageBytes);
                    
                    if (btnUploadSkin != null) btnUploadSkin.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    NotificationManager.Show("LỖI ĐỌC FILE", ex.Message);
                }
            }
        }

        private async void btnUploadSkin_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedSkinBase64)) return;

            if (btnUploadSkin != null)
            {
                btnUploadSkin.IsEnabled = false;
                btnUploadSkin.Content = "ĐANG TẢI...";
            }

            var payload = new { username = _username, skinBase64 = _selectedSkinBase64 };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/upload-skin", content);
                var responseString = await response.Content.ReadAsStringAsync();
                
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(async () =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show("THÀNH CÔNG", "Cập nhật Skin thành công!");
                        _selectedSkinBase64 = ""; 
                        if (btnUploadSkin != null) btnUploadSkin.IsEnabled = false;
                        
                        // THÊM DÒNG NÀY: Trả khung ảnh preview về trạng thái trống
                        if (imgSkinPreview != null) imgSkinPreview.Source = null;
                        
                        await LoadUserSkinAsync();
                    }
                    else
                    {
                        NotificationManager.Show("LỖI", result?.Message ?? "Lỗi tải skin!");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => NotificationManager.Show("LỖI KẾT NỐI", ex.Message));
            }
            finally
            {
                Dispatcher.Invoke(() => {
                    if (btnUploadSkin != null) btnUploadSkin.Content = "TẢI LÊN";
                });
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
                Dispatcher.Invoke(() => NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập đủ mật khẩu cũ và mới!"));
                return;
            }

            var payload = new { username = _username, oldPassword = oldPass, newPassword = newPass };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/change-password", content);
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show("THÀNH CÔNG", "Đổi mật khẩu thành công!");
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
                        NotificationManager.Show("LỖI", result?.Message ?? "Mật khẩu cũ không chính xác!");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => NotificationManager.Show("LỖI KẾT NỐI", ex.Message));
            }
        }

        // ------------------ ĐỔI MẬT KHẨU ------------------
        private async void btnSendPasswordOtp_Click(object sender, RoutedEventArgs e)
        {
            string oldPass = txtOldPassword.Password;
            string newPass = txtNewPassword.Password;
            string confirmPass = txtConfirmNewPassword.Password;

            if (string.IsNullOrEmpty(oldPass) || string.IsNullOrEmpty(newPass) || string.IsNullOrEmpty(confirmPass))
            {
                Dispatcher.Invoke(() => NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập đầy đủ Mật khẩu hiện tại, Mật khẩu mới và Xác nhận!"));
                return;
            }

            if (newPass != confirmPass)
            {
                Dispatcher.Invoke(() => NotificationManager.Show("CẢNH BÁO", "Mật khẩu xác nhận không khớp!"));
                return;
            }

            btnSendPasswordOtp.IsEnabled = false;
            btnSendPasswordOtp.Content = "ĐANG GỬI...";

            var payload = new { username = _username, oldPassword = oldPass };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/request-password-otp", content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (responseString.Trim().StartsWith("<"))
                {
                    Dispatcher.Invoke(() => {
                        NotificationManager.Show("LỖI BACKEND", "Máy chủ báo lỗi. Vui lòng kiểm tra Terminal!");
                        btnSendPasswordOtp.IsEnabled = true;
                        btnSendPasswordOtp.Content = "ĐỔI MẬT KHẨU";
                    });
                    return;
                }

                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() => 
                {
                    btnSendPasswordOtp.IsEnabled = true;
                    btnSendPasswordOtp.Content = "ĐỔI MẬT KHẨU";

                    if (result != null && result.Success)
                    {
                        NotificationManager.Show("THÀNH CÔNG", "Đã gửi mã OTP đến Email Gốc của bạn!");
                        
                        if (pnlPasswordInput != null && pnlPasswordOtpVerify != null)
                            PlayTransitionAnimation(pnlPasswordOtpVerify, pnlPasswordInput);
                    }
                    else 
                    {
                        NotificationManager.Show("LỖI", result?.Message ?? "Mật khẩu hiện tại không chính xác!");
                    }
                });
            }
            catch (Exception ex) 
            { 
                Dispatcher.Invoke(() => {
                    NotificationManager.Show("LỖI KẾT NỐI", ex.Message);
                    btnSendPasswordOtp.IsEnabled = true;
                    btnSendPasswordOtp.Content = "ĐỔI MẬT KHẨU";
                }); 
            }
        }

        private void btnCancelPasswordOtp_Click(object sender, RoutedEventArgs e)
        {
            if (txtOldPassword != null) txtOldPassword.Password = "";
            if (txtNewPassword != null) txtNewPassword.Password = "";
            if (txtConfirmNewPassword != null) txtConfirmNewPassword.Password = "";
            if (txtPasswordOtp != null) txtPasswordOtp.Text = "";

            if (pnlPasswordInput != null && pnlPasswordOtpVerify != null)
                PlayTransitionAnimation(pnlPasswordInput, pnlPasswordOtpVerify);
        }

        private async void btnConfirmPasswordChange_Click(object sender, RoutedEventArgs e)
        {
            string newPass = txtNewPassword.Password;
            string otp = txtPasswordOtp.Text;

            if (string.IsNullOrEmpty(otp))
            {
                Dispatcher.Invoke(() => NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập mã OTP!"));
                return;
            }

            var payload = new { username = _username, otp = otp, newPassword = newPass };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/reset-password", content);
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() => 
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show("THÀNH CÔNG", "Đổi mật khẩu thành công!");
                        
                        if (txtOldPassword != null) txtOldPassword.Password = "";
                        if (txtNewPassword != null) txtNewPassword.Password = "";
                        if (txtConfirmNewPassword != null) txtConfirmNewPassword.Password = "";
                        if (txtPasswordOtp != null) txtPasswordOtp.Text = "";
                        
                        if (pnlPasswordInput != null && pnlPasswordOtpVerify != null)
                            PlayTransitionAnimation(pnlPasswordInput, pnlPasswordOtpVerify);
                        
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
                        NotificationManager.Show("LỖI", result?.Message ?? "Mã OTP Sai hoặc đã hết hạn!");
                    }
                });
            }
            catch (Exception ex) 
            { 
                Dispatcher.Invoke(() => NotificationManager.Show("LỖI KẾT NỐI", ex.Message)); 
            }
        }

        // ------------------ ĐỔI EMAIL ------------------
        private async void btnSendEmailOtp_Click(object sender, RoutedEventArgs e)
        {
            string newEmail = txtNewEmail.Text;
            if (string.IsNullOrEmpty(newEmail)) 
            {
                Dispatcher.Invoke(() => NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập Email mới!"));
                return;
            }

            btnSendEmailOtp.IsEnabled = false;
            btnSendEmailOtp.Content = "ĐANG GỬI...";

            var payload = new { username = _username, newEmail = newEmail };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/request-email-change", content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (responseString.Trim().StartsWith("<"))
                {
                    Dispatcher.Invoke(() => {
                        NotificationManager.Show("LỖI BACKEND", "Máy chủ báo lỗi. Vui lòng kiểm tra Terminal!");
                        btnSendEmailOtp.IsEnabled = true;
                        btnSendEmailOtp.Content = "ĐỔI EMAIL";
                    });
                    return;
                }

                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() => 
                {
                    btnSendEmailOtp.IsEnabled = true;
                    btnSendEmailOtp.Content = "ĐỔI EMAIL";

                    if (result != null && result.Success)
                    {
                        NotificationManager.Show("THÀNH CÔNG", "Đã gửi mã OTP đến Email Gốc của bạn!");
                        
                        if (pnlEmailInput != null && pnlOtpVerify != null)
                            PlayTransitionAnimation(pnlOtpVerify, pnlEmailInput);
                    }
                    else 
                    {
                        NotificationManager.Show("LỖI", result?.Message ?? "Lỗi gửi email!");
                    }
                });
            }
            catch (Exception ex) 
            { 
                Dispatcher.Invoke(() => {
                    NotificationManager.Show("LỖI KẾT NỐI", ex.Message);
                    btnSendEmailOtp.IsEnabled = true;
                    btnSendEmailOtp.Content = "ĐỔI EMAIL";
                }); 
            }
        }

        private void btnCancelEmailOtp_Click(object sender, RoutedEventArgs e)
        {
            if (txtNewEmail != null) txtNewEmail.Text = "";
            if (txtEmailOtp != null) txtEmailOtp.Text = "";

            if (pnlEmailInput != null && pnlOtpVerify != null)
                PlayTransitionAnimation(pnlEmailInput, pnlOtpVerify);
        }

        private async void btnConfirmEmailChange_Click(object sender, RoutedEventArgs e)
        {
            string newEmail = txtNewEmail.Text;
            string otp = txtEmailOtp.Text;

            if (string.IsNullOrEmpty(newEmail) || string.IsNullOrEmpty(otp))
            {
                Dispatcher.Invoke(() => NotificationManager.Show("CẢNH BÁO", "Vui lòng nhập đầy đủ Email mới và OTP!"));
                return;
            }

            var payload = new { username = _username, newEmail = newEmail, otp = otp };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{API_SERVER_URL}/auth/change-email", content);
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() => 
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show("THÀNH CÔNG", "Cập nhật Email khôi phục thành công!");
                        
                        if (txtNewEmail != null) txtNewEmail.Text = "";
                        if (txtEmailOtp != null) txtEmailOtp.Text = "";
                        
                        if (pnlEmailInput != null && pnlOtpVerify != null)
                            PlayTransitionAnimation(pnlEmailInput, pnlOtpVerify);
                    }
                    else 
                    {
                        NotificationManager.Show("LỖI", result?.Message ?? "Mã OTP Sai!");
                    }
                });
            }
            catch (Exception ex) 
            { 
                Dispatcher.Invoke(() => NotificationManager.Show("LỖI KẾT NỐI", ex.Message)); 
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
            var skinPanel = (FrameworkElement)this.FindName("pnlProfileSkin");
            PlayTransitionAnimation(pnlProfileInfo, pnlProfileEmail, pnlProfilePassword, skinPanel);
        }

        private void SubTabSkin_Checked(object sender, RoutedEventArgs e)
        {
            var skinPanel = (FrameworkElement)this.FindName("pnlProfileSkin");
            if (skinPanel == null) return;
            PlayTransitionAnimation(skinPanel, pnlProfileInfo, pnlProfileEmail, pnlProfilePassword);
        }

        private void SubTabEmail_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlProfileEmail == null) return;
            var skinPanel = (FrameworkElement)this.FindName("pnlProfileSkin");
            PlayTransitionAnimation(pnlProfileEmail, pnlProfileInfo, pnlProfilePassword, skinPanel);
            
            if (this.FindName("pnlOtpVerify") is FrameworkElement otpPanel) otpPanel.Visibility = Visibility.Collapsed;
            if (this.FindName("pnlEmailInput") is FrameworkElement emailInputPanel) emailInputPanel.Visibility = Visibility.Visible;
            if (txtNewEmail != null) txtNewEmail.Text = "";
            if (this.FindName("txtEmailOtp") is System.Windows.Controls.TextBox txtOtp) txtOtp.Text = "";
        }

        private void SubTabPassword_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlProfilePassword == null) return;
            var skinPanel = (FrameworkElement)this.FindName("pnlProfileSkin");
            PlayTransitionAnimation(pnlProfilePassword, pnlProfileInfo, pnlProfileEmail, skinPanel);
            
            if (this.FindName("pnlPasswordOtpVerify") is FrameworkElement otpVerifyPanel) otpVerifyPanel.Visibility = Visibility.Collapsed;
            if (this.FindName("pnlPasswordInput") is FrameworkElement passInputPanel) passInputPanel.Visibility = Visibility.Visible;
            if (txtOldPassword != null) txtOldPassword.Password = "";
            if (txtNewPassword != null) txtNewPassword.Password = "";
            if (txtConfirmNewPassword != null) txtConfirmNewPassword.Password = "";
            if (this.FindName("txtPasswordOtp") is System.Windows.Controls.TextBox txtPassOtp) txtPassOtp.Text = "";
        }

        // ===============================================================
        // CÁC SỰ KIỆN ĐIỀU KHIỂN CỬA SỔ CHUNG
        // ===============================================================

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { this.DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSyncTimer?.Stop();
            Application.Current.Shutdown();
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSyncTimer?.Stop();

            if (File.Exists(SESSION_FILE))
            {
                File.Delete(SESSION_FILE);
            }

            this.IsHitTestVisible = false;
            var fadeOutHome = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fadeOutHome.Completed += (s, ev) =>
            {
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
    public class ModStatusItem
    {
        public string FileName { get; set; }
        public bool IsInstalled { get; set; }
        public string StatusIcon => IsInstalled ? "✔" : "❌";
        public string StatusColor => IsInstalled ? "#4ADE80" : "#FF4D4D";
    }
}
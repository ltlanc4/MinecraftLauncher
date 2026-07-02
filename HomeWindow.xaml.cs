using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;
using Image = System.Windows.Controls.Image;
using CmlLib.Core;
using CmlLib.Core.Auth;
using Microsoft.Win32;
using System.Windows.Media.Imaging;
using DotNetEnv;
using System.Reflection;

namespace MinecraftLauncher
{
    public partial class HomeWindow : Window
    {
        private readonly string CURRENT_VERSION = "1.0.2-hotfix";
        private string _username;
        private static readonly HttpClient _httpClient = new HttpClient();

        private string API_SERVER_URL;
        private readonly string _appDataFolder;
        private readonly string SESSION_FILE;

        // KHAI BÁO BIẾN QUẢN LÝ CÀI ĐẶT CHUNG (JSON)
        private readonly string SETTINGS_FILE;
        private LauncherSettings _appSettings = new LauncherSettings();
        private bool _isSettingsLoaded = false; // Chốt khóa an toàn chặn ghi file lúc đang nạp UI

        private ServerInfoResponse _serverManifest;
        private bool _isInstalled = false;
        private string _minecraftDirectory;

        private DispatcherTimer _autoSyncTimer;
        private FileSystemWatcher _modsWatcher;
        private string _selectedSkinBase64 = "";

        // TỪ ĐIỂN LƯU TRỮ NGÔN NGỮ
        private bool _isEnglish = false;
        private Dictionary<string, string> _langDict = new Dictionary<string, string>();

        // BIẾN ĐỂ NHỚ ID CỦA GAME ĐANG CHẠY
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private int _activeGamePid = -1;

        // Khai báo Win32 API để chữa bệnh "Menu không chịu đóng khi click ra ngoài" của WPF
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public HomeWindow(string username, string token, string uuid)
        {
            InitializeComponent();

            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher");
            if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);

            SESSION_FILE = Path.Combine(_appDataFolder, "session_data.json");
            SETTINGS_FILE = Path.Combine(_appDataFolder, "launcher_settings.json"); // File gom chung

            // --- 1. CHIẾN DỊCH "TÌM VÀ DIỆT" FILE .ENV VẬT LÝ (BẢO MẬT) ---
            string envPath = Path.Combine(_appDataFolder, ".env");
            if (File.Exists(envPath))
            {
                try { File.Delete(envPath); } catch { } // Bọc try-catch để lỡ file đang bị Windows khóa cũng không làm crash app
            }

            // --- 2. NẠP NỘI SOI FILE NHÚNG TRỰC TIẾP VÀO RAM ---
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("MinecraftLauncher.default.env"))
            {
                if (stream != null)
                {
                    // Nạp thẳng luồng Stream vào biến môi trường của Windows
                    Env.Load(stream);
                }
            }

            string serverIP = Env.GetString("SERVER_API_IP");
            string serverPort = Env.GetString("SERVER_API_PORT");
            API_SERVER_URL = $"http://{serverIP}:{serverPort}";
            Application.Current.MainWindow = this;

            _username = username;
            txtUsername.Text = username.ToUpper();
            if (txtProfileDisplayUsername != null) txtProfileDisplayUsername.Text = username;
            if (txtProfileUsernameValue != null) txtProfileUsernameValue.Text = username;

            // Nạp toàn bộ cài đặt từ file JSON
            LoadLauncherSettings();

            _autoSyncTimer = new DispatcherTimer();
            _autoSyncTimer.Interval = TimeSpan.FromSeconds(3);
            _autoSyncTimer.Tick += async (s, ev) => await AutoCheckServerUpdates();
            _autoSyncTimer.Start();
        }

        // ================= HỆ THỐNG LƯU TRỮ JSON =================
        private void LoadLauncherSettings()
        {
            if (File.Exists(SETTINGS_FILE))
            {
                try
                {
                    string json = File.ReadAllText(SETTINGS_FILE);
                    _appSettings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
                }
                catch { } // Nếu lỗi format, tự động dùng cấu hình mặc định
            }

            // 1. Áp dụng Ngôn ngữ
            _isEnglish = _appSettings.Language == "EN";
            ApplyLanguage();

            // 2. Áp dụng Đường dẫn cài đặt
            if (string.IsNullOrEmpty(_appSettings.InstallPath))
            {
                _appSettings.InstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft");
            }
            _minecraftDirectory = EnsureMinecraftDirectory(_appSettings.InstallPath);
            txtInstallPath.Text = _minecraftDirectory;

            // 3. Áp dụng RAM lên thanh trượt
            if (this.FindName("sliderRam") is Slider sld) sld.Value = _appSettings.AllocatedRam;
            if (this.FindName("txtRamValue") is TextBlock txtRam) txtRam.Text = $"{_appSettings.AllocatedRam} MB ({_appSettings.AllocatedRam / 1024.0:0.#} GB)";
            if (this.FindName($"radCloseMode{_appSettings.CloseMode}") is RadioButton radClose) radClose.IsChecked = true;

            // 4. Áp dụng Đồ họa lên nút tick
            // if (this.FindName($"radGraphics{_appSettings.GraphicsPreset}") is RadioButton rad) rad.IsChecked = true;

            // Mở khóa cho phép lưu file khi có thao tác mới
            _isSettingsLoaded = true;
        }

        // ================= CỖ MÁY BĂM MD5 CHO FILE (ĐÃ NÂNG CẤP CHỐNG KHÓA FILE) =================
        private string GetFileMD5(string filePath)
        {
            try
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch
            {
                return "ERR"; // Trả về ERR nếu file bị Windows/tiến trình khác khóa
            } 
        }

        private void SaveLauncherSettings()
        {
            if (!_isSettingsLoaded) return; // Chặn ghi đè lúc UI đang nạp
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true }; // Lưu JSON thụt lề cho đẹp
                File.WriteAllText(SETTINGS_FILE, JsonSerializer.Serialize(_appSettings, options));
            }
            catch { }
        }

        private void sliderRam_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int newRam = (int)e.NewValue;
            if (this.FindName("txtRamValue") is TextBlock txt)
            {
                txt.Text = $"{newRam} MB ({newRam / 1024.0:0.#} GB)";
            }

            if (_isSettingsLoaded)
            {
                _appSettings.AllocatedRam = newRam;
                SaveLauncherSettings();
            }
        }

        // private void GraphicsPreset_Checked(object sender, RoutedEventArgs e)
        // {
        //     if (!_isSettingsLoaded) return;
        //     if (sender is RadioButton rad && rad.Tag != null)
        //     {
        //         _appSettings.GraphicsPreset = rad.Tag.ToString();
        //         SaveLauncherSettings();
        //     }
        // }

        private string EnsureMinecraftDirectory(string path)
        {
            if (!path.EndsWith("Minecraft", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "Minecraft");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return path;
        }

        private void SaveMinecraftPath(string path)
        {
            _minecraftDirectory = EnsureMinecraftDirectory(path);
            txtInstallPath.Text = _minecraftDirectory;

            _appSettings.InstallPath = _minecraftDirectory;
            SaveLauncherSettings();

            CheckInstallationStatus();
        }

        private void ChangePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = GetLang("msg_SelectFolder");
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) SaveMinecraftPath(dialog.SelectedPath);
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
                _appSettings.Language = _isEnglish ? "EN" : "VI";
                SaveLauncherSettings(); // LƯU VÀO JSON

                string currentExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(currentExecutablePath);

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

            // 1. TỰ ĐỘNG QUÉT VÀ GÁN TOÀN BỘ CHỮ: 
            // Chỉ cần tên x:Name trong XAML trùng với Key trong file .pak là chữ tự động vào đúng chỗ!
            foreach (var kvp in _langDict)
            {
                var element = this.FindName(kvp.Key);
                if (element is TextBlock tb) tb.Text = kvp.Value;
                else if (element is Button btn) btn.Content = kvp.Value;
                else if (element is RadioButton rad) rad.Content = kvp.Value;
                else if (element is MenuItem mnu) mnu.Header = kvp.Value;
            }

            // 2. NHỮNG THÀNH PHẦN ĐẶC BIỆT KHÔNG THỂ GÁN TỰ ĐỘNG:
            if (this.FindName("btnSettings") is Button btnSet) btnSet.ToolTip = GetLang("btnSettingsTooltip");

            // ContextMenu của Tray Icon nằm trong Window.Resources nên lệnh FindName() không chui vào quét được, ta phải quét thủ công nó:
            if (this.FindResource("TrayContextMenu") is ContextMenu trayMenu)
            {
                foreach (var item in trayMenu.Items)
                {
                    if (item is MenuItem menuItem && !string.IsNullOrEmpty(menuItem.Name) && _langDict.ContainsKey(menuItem.Name))
                    {
                        menuItem.Header = _langDict[menuItem.Name];
                    }
                }
            }

            // 3. CẬP NHẬT LẠI TRẠNG THÁI HIỂN THỊ
            CheckInstallationStatus();
            UpdateTotalModsText();
        }

        private string GetLang(string key)
        {
            if (_langDict != null && _langDict.ContainsKey(key)) return _langDict[key];
            return key;
        }

        private void UpdateTotalModsText()
        {
            var txtMods = this.FindName("txtTotalMods") as TextBlock;
            if (txtMods == null) return;

            if (_serverManifest == null) txtMods.Text = GetLang("msg_ReadingManifest");
            else if (_serverManifest.Success) txtMods.Text = string.Format(GetLang("msg_CheckedSync"), _serverManifest.TotalMods);
            else txtMods.Text = GetLang("msg_CannotLoadMod");
        }

        // ================= TẢI DỮ LIỆU TỪ SERVER =================
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadServerInfoFromManifest();
            await LoadUserSkinAsync();
            await CheckForLauncherUpdate();
            InitializeNotifyIcon();
            ScanAndHookRunningGame();
        }

        private async Task LoadServerInfoFromManifest()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{API_SERVER_URL}/auth/server-info");
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
                    _serverManifest = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, jsonOptions);

                    if (_serverManifest != null && _serverManifest.Success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            txtGameVersion.Text = $"{_serverManifest.Loader} {_serverManifest.Version}";
                            UpdateTotalModsText();
                        });
                        CheckInstallationStatus();
                        return;
                    }
                }
                Dispatcher.Invoke(() => { txtGameVersion.Text = GetLang("msg_OfflineConfig"); UpdateTotalModsText(); });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    txtGameVersion.Text = GetLang("msg_SystemError");
                    if (this.FindName("txtTotalMods") is TextBlock txtMods) txtMods.Text = string.Format(GetLang("msg_ErrorDetail"), ex.Message);
                });
            }
        }

        private async Task AutoCheckServerUpdates()
        {
            if (!btnPlay.IsEnabled) return;
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{API_SERVER_URL}/auth/server-info");
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
                    var newManifest = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, jsonOptions);

                    if (newManifest != null && newManifest.Success)
                    {
                        bool isChanged = false;
                        if (_serverManifest == null || _serverManifest.TotalMods != newManifest.TotalMods) isChanged = true;
                        else if (_serverManifest.Mods != null && newManifest.Mods != null)
                        {
                            // Đọ Hash thay vì đọ Tên
                            var oldHashes = _serverManifest.Mods.Select(m => m.Hash).OrderBy(h => h).ToList();
                            var newHashes = newManifest.Mods.Select(m => m.Hash).OrderBy(h => h).ToList();
                            if (!oldHashes.SequenceEqual(newHashes)) isChanged = true;
                        }

                        if (isChanged)
                        {
                            _serverManifest = newManifest;
                            Dispatcher.Invoke(() =>
                            {
                                txtGameVersion.Text = $"{_serverManifest.Loader} {_serverManifest.Version}";
                                UpdateTotalModsText();
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
                return $"fabric-loader-{_serverManifest.Loader_Version}-{_serverManifest.Version}";
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

            if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);

            if (_modsWatcher == null || _modsWatcher.Path != modsDir)
            {
                if (_modsWatcher != null) { _modsWatcher.EnableRaisingEvents = false; _modsWatcher.Dispose(); }
                _modsWatcher = new FileSystemWatcher(modsDir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };

                FileSystemEventHandler onModChanged = (s, e) =>
                {
                    Dispatcher.Invoke(() => { if (btnPlay != null && btnPlay.IsEnabled) CheckInstallationStatus(); });
                };
                _modsWatcher.Deleted += onModChanged;
                _modsWatcher.Created += onModChanged;
                _modsWatcher.Renamed += new RenamedEventHandler(onModChanged);
                _modsWatcher.EnableRaisingEvents = true;
            }

            bool isTargetGameInstalled = File.Exists(jsonFile);
            bool hasObsoleteVersion = false;
            
            if (Directory.Exists(path.Versions))
            {
                var obsoleteDirs = Directory.GetDirectories(path.Versions)
                    .Where(d => !Path.GetFileName(d).Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!isTargetGameInstalled && obsoleteDirs.Count > 0) hasObsoleteVersion = true;
            }

            bool areModsInstalled = true;
            var displayList = new List<ModStatusItem>();

            if (_serverManifest.Mods != null && _serverManifest.Mods.Count > 0)
            {
                var localFiles = Directory.GetFiles(modsDir).Select(Path.GetFileName).ToList();
                var serverModNames = _serverManifest.Mods.Select(m => m.Name).ToList();

                // 1. Cảm biến phát hiện dư file/thiếu file (Kháng lỗi chữ hoa chữ thường)
                if (serverModNames.Except(localFiles, StringComparer.OrdinalIgnoreCase).Any() || 
                    localFiles.Except(serverModNames, StringComparer.OrdinalIgnoreCase).Any()) 
                {
                    areModsInstalled = false;
                }
                
                // 2. KHÔI PHỤC CẢM BIẾN QUÉT MD5 CHO TỪNG FILE
                foreach (var serverMod in _serverManifest.Mods) 
                {
                    bool isInstalled = false;

                    // Tìm xem dưới máy khách có file nào trùng tên không (bỏ qua hoa/thường)
                    string localFileName = localFiles.FirstOrDefault(f => string.Equals(f, serverMod.Name, StringComparison.OrdinalIgnoreCase));
                    
                    if (!string.IsNullOrEmpty(localFileName))
                    {
                        string localFilePath = Path.Combine(modsDir, localFileName);
                        string localHash = GetFileMD5(localFilePath); // Đọc ruột file
                        string serverHash = serverMod.Hash?.Trim()?.ToLowerInvariant() ?? "";

                        // CHỈ CÔNG NHẬN KHI MÃ MD5 CỦA MÁY KHÁCH VÀ SERVER HOÀN TOÀN TRÙNG KHỚP
                        if (!string.IsNullOrEmpty(localHash) && localHash != "err" && localHash == serverHash)
                        {
                            isInstalled = true;
                        }
                    }

                    // Nếu có 1 file sai Hash -> Ngay lập tức đánh dấu bộ Mod bị lỗi
                    if (!isInstalled) areModsInstalled = false;

                    displayList.Add(new ModStatusItem { FileName = serverMod.Name, IsInstalled = isInstalled });
                }
            }

            _isInstalled = isTargetGameInstalled && areModsInstalled && !hasObsoleteVersion;

            Dispatcher.Invoke(() =>
            {
                if (this.FindName("icModsList") is ItemsControl list) list.ItemsSource = displayList;
                if (btnPlay.Content != null && (btnPlay.Content.ToString() == GetLang("btn_Playing"))) return;

                var label = this.FindName("txtVersionLabel") as TextBlock;

                if (_isInstalled)
                {
                    btnPlay.Content = GetLang("btn_Play");
                    if (label != null) label.Text = GetLang("lbl_CurrentVersion");
                }
                else if (hasObsoleteVersion) 
                {
                    btnPlay.Content = GetLang("btn_Update");
                    if (label != null) label.Text = _isEnglish ? "Major upgrade required:" : "Yêu cầu nâng cấp phiên bản:";
                }
                else if (isTargetGameInstalled && !areModsInstalled)
                {
                    btnPlay.Content = GetLang("btn_Update");
                    if (label != null) label.Text = GetLang("lbl_VersionToDownload");
                }
                else
                {
                    btnPlay.Content = GetLang("btn_Install");
                    if (label != null) label.Text = GetLang("lbl_VersionToDownload");
                }
            });
        }

        private async Task SyncModsAsync(string modsDirectory)
        {
            if (_serverManifest == null || _serverManifest.Mods == null || _serverManifest.Mods.Count == 0) return;
            if (!Directory.Exists(modsDirectory)) Directory.CreateDirectory(modsDirectory);

            var localFiles = Directory.GetFiles(modsDirectory).Select(Path.GetFileName).ToList();
            var serverMods = _serverManifest.Mods;
            var modsToDownload = new List<string>();

            // ==========================================================
            // BƯỚC 1: QUÉT SÂU - VỪA DỌN FILE RÁC, VỪA LỌC HASH MD5
            // ==========================================================
            foreach (var localFile in localFiles)
            {
                var serverMod = serverMods.FirstOrDefault(m => string.Equals(m.Name, localFile, StringComparison.OrdinalIgnoreCase));
                string fullPath = Path.Combine(modsDirectory, localFile);

                if (serverMod == null)
                {
                    // Nếu Server không có file này -> Đây là file rác -> Xóa
                    try { File.Delete(fullPath); } catch { }
                }
                else
                {
                    // Nếu trùng tên -> Đưa vào máy quét X-Quang MD5
                    string localHash = GetFileMD5(fullPath);
                    string serverHash = serverMod.Hash?.Trim()?.ToLowerInvariant() ?? "";

                    // Nếu mã Hash lệch nhau (nghĩa là ruột file đã bị thay đổi)
                    if (!string.IsNullOrEmpty(localHash) && localHash != "err" && localHash != serverHash)
                    {
                        // Xóa cặn bã file cũ đi và ghi tên nó vào sổ chờ tải lại
                        try { File.Delete(fullPath); } catch { }
                        modsToDownload.Add(serverMod.Name);
                    }
                }
            }

            // ==========================================================
            // BƯỚC 2: RÀ SOÁT - TÌM NHỮNG FILE HOÀN TOÀN CHƯA CÓ TRONG MÁY
            // ==========================================================
            // Cập nhật lại danh sách file trong máy sau khi đã dọn dẹp ở Bước 1
            var validLocalFiles = Directory.GetFiles(modsDirectory).Select(Path.GetFileName).ToList();
            var serverModNames = serverMods.Select(m => m.Name).ToList();

            var missingFiles = serverModNames.Except(validLocalFiles, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var missing in missingFiles)
            {
                if (!modsToDownload.Contains(missing))
                {
                    modsToDownload.Add(missing);
                }
            }

            // ==========================================================
            // BƯỚC 3: TIẾN HÀNH TẢI CHÍNH XÁC NHỮNG FILE BỊ THIẾU / HỎNG
            // ==========================================================
            if (modsToDownload.Count > 0)
            {
                var pBar = this.FindName("pbDownload") as ProgressBar;
                Dispatcher.Invoke(() => { if (pBar != null) { pBar.Maximum = modsToDownload.Count; pBar.Value = 0; } });

                for (int i = 0; i < modsToDownload.Count; i++)
                {
                    string modFile = modsToDownload[i];
                    Dispatcher.Invoke(() =>
                    {
                        if (FindName("txtDownloadStatus") is TextBlock txtStat) txtStat.Text = string.Format(GetLang("msg_DownloadingMod"), modFile);
                        if (FindName("txtDownloadDetail") is TextBlock txtDet) txtDet.Text = $"{i + 1} / {modsToDownload.Count}";
                        if (FindName("txtDownloadPercentage") is TextBlock txtPct) txtPct.Text = $"{(double)(i + 1) / modsToDownload.Count * 100:F0}%";
                    });

                    try
                    {
                        // Kẹp thêm Time Ticks để đâm thủng lớp Cache bảo vệ của Cloudflare/VPS
                        byte[] fileBytes = await _httpClient.GetByteArrayAsync($"{API_SERVER_URL}/mods/{modFile}?t={DateTime.Now.Ticks}");
                        await File.WriteAllBytesAsync(Path.Combine(modsDirectory, modFile), fileBytes);
                    }
                    catch { }

                    Dispatcher.Invoke(() => { if (pBar != null) pBar.Value = i + 1; });
                }
            }
        }


        // ================= XỬ LÝ KHỞI ĐỘNG TRÒ CHƠI =================
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serverManifest == null)
            {
                Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_ConnectionError"), GetLang("msg_LoadConfigError")));
                return;
            }

            btnPlay.IsEnabled = false;
            var progressContainer = this.FindName("DownloadProgressContainer") as FrameworkElement;
            if (progressContainer != null) progressContainer.Visibility = Visibility.Visible;
            var pBar = this.FindName("pbDownload") as ProgressBar;
            if (pBar != null) pBar.Value = 0;

            try
            {
                var path = new MinecraftPath(_minecraftDirectory);
                var launcher = new CMLauncher(path);

                string targetVersion = GetTargetVersionName();

                if (!_isInstalled)
                {
                    // ====================================================================
                    // CHIẾN DỊCH "STATELESS WIPE" CHO CLIENT ONLINE-ONLY
                    // ====================================================================
                    if (Directory.Exists(path.Versions))
                    {
                        var obsoleteDirs = Directory.GetDirectories(path.Versions)
                            // BỎ QUA thư mục đích (Fabric) VÀ BỎ QUA thư mục Vanilla gốc
                            .Where(d => !Path.GetFileName(d).Equals(targetVersion, StringComparison.OrdinalIgnoreCase) && 
                                        !Path.GetFileName(d).Equals(_serverManifest.Version, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (obsoleteDirs.Count > 0)
                        {
                            Dispatcher.Invoke(() => { 
                                if (this.FindName("txtDownloadStatus") is TextBlock t) 
                                    t.Text = _isEnglish ? "Cleaning up old client versions..." : "Đang dọn dẹp phân vùng phiên bản cũ..."; 
                            });

                            // 1. Chỉ gỡ bỏ folder core phiên bản cũ
                            foreach (var oldDir in obsoleteDirs)
                            {
                                try { Directory.Delete(oldDir, true); } catch { }
                            }

                            // TUYỆT ĐỐI KHÔNG XÓA THƯ MỤC /mods Ở ĐÂY NỮA! 
                            // Việc xóa mod cũ sẽ giao cho hàm SyncModsAsync đảm nhận.

                            // 2. Tiêu diệt folder /saves (Xóa vĩnh viễn tàn dư thế giới Singleplayer)
                            string savesDir = Path.Combine(path.BasePath, "saves");
                            if (Directory.Exists(savesDir))
                            {
                                try { Directory.Delete(savesDir, true); } catch { }
                            }

                            await Task.Delay(800); 
                        }
                    }
                    // ====================================================================

                    if (targetVersion.StartsWith("fabric-loader"))
                    {
                        string versionDir = Path.Combine(path.Versions, targetVersion);
                        string jsonPath = Path.Combine(versionDir, targetVersion + ".json");

                        if (!File.Exists(jsonPath))
                        {
                            Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_FetchFabric"); });
                            if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

                            try
                            {
                                byte[] jsonBytes = await _httpClient.GetByteArrayAsync($"https://meta.fabricmc.net/v2/versions/loader/{_serverManifest.Version}/{_serverManifest.Loader_Version}/profile/json");
                                await File.WriteAllBytesAsync(jsonPath, jsonBytes);
                            }
                            catch (Exception ex) { throw new Exception(GetLang("msg_CannotFetchFabric") + ex.Message); }
                        }
                    }

                    Dispatcher.Invoke(() => { btnPlay.Content = GetLang("btn_Downloading"); if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_InstallCore"); });
                }
                else
                {
                    Dispatcher.Invoke(() => { btnPlay.Content = GetLang("btn_Checking"); if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_CheckUpdate"); });
                }

                launcher.FileChanged += (fileEvent) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (this.FindName("txtDownloadStatus") is TextBlock txtStat) txtStat.Text = $"[{fileEvent.FileKind}] {fileEvent.FileName}";
                        if (this.FindName("txtDownloadDetail") is TextBlock txtDet) txtDet.Text = $"{fileEvent.ProgressedFileCount} / {fileEvent.TotalFileCount}";
                        if (pBar != null) { pBar.Maximum = fileEvent.TotalFileCount; pBar.Value = fileEvent.ProgressedFileCount; }
                        double pct = fileEvent.TotalFileCount > 0 ? ((double)fileEvent.ProgressedFileCount / fileEvent.TotalFileCount) * 100 : 0;
                        if (this.FindName("txtDownloadPercentage") is TextBlock txtPct) txtPct.Text = $"{pct:F0}%";
                    });
                };

                bool isFullScreen = false;
                string optionsFile = Path.Combine(path.BasePath, "options.txt");
                if (File.Exists(optionsFile))
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(optionsFile))
                            if (line.StartsWith("fullscreen:")) { isFullScreen = line.Split(':')[1].Trim().ToLower() == "true"; break; }
                    }
                    catch { }
                }

                var launchOption = new MLaunchOption
                {
                    Session = MSession.CreateOfflineSession(_username),

                    // Khai báo chuẩn của CmlLib
                    MaximumRamMb = _appSettings.AllocatedRam,
                    MinimumRamMb = _appSettings.AllocatedRam, // Ép Min = Max để tránh Windows co giãn Heap gây khựng game

                    // TUYỆT CHIÊU "KHÓA ĐUÔI": Ghì chết tham số JVM ở cuối chuỗi lệnh
                    JVMArguments = new string[]
                    {
                        $"-Xms{_appSettings.AllocatedRam}m",
                        $"-Xmx{_appSettings.AllocatedRam}m",
                        "-XX:+UseG1GC", // Kích hoạt bộ dọn rác G1GC xịn nhất cho Minecraft
                        "-Dsun.rmi.dgc.server.gcInterval=2147483646", // Chống rớt TPS đột ngột
                        "-XX:+UnlockExperimentalVMOptions",
                        "-XX:G1NewSizePercent=20",
                        "-XX:G1ReservePercent=20",
                        "-XX:MaxGCPauseMillis=50",
                        "-XX:G1HeapRegionSize=32M"
                    },

                    ServerIp = string.IsNullOrEmpty(_serverManifest.Server_Ip) ? "127.0.0.1" : _serverManifest.Server_Ip,
                    ServerPort = _serverManifest.Server_Port > 0 ? _serverManifest.Server_Port : 25565
                };

                if (isFullScreen) launchOption.FullScreen = true;
                else { launchOption.ScreenWidth = (int)SystemParameters.WorkArea.Width; launchOption.ScreenHeight = (int)SystemParameters.WorkArea.Height; }

                var process = await launcher.CreateProcessAsync(targetVersion, launchOption);

                Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_SyncServer"); });
                await SyncModsAsync(Path.Combine(path.BasePath, "mods"));

                Dispatcher.Invoke(() => CheckInstallationStatus());

                Dispatcher.Invoke(() => { 
                    if (this.FindName("txtDownloadStatus") is TextBlock t) 
                        t.Text = GetLang("msg_ConfigSkin"); 
                });
                
                try
                {
                    string cslFolder = Path.Combine(path.BasePath, "CustomSkinLoader");
                    if (!Directory.Exists(cslFolder)) Directory.CreateDirectory(cslFolder);

                    // 1. Phá Cache: Xóa sạch ảnh cũ ngâm trong RAM/Ổ cứng của máy trạm
                    string cslCacheFolder = Path.Combine(cslFolder, "caches");
                    if (Directory.Exists(cslCacheFolder)) { try { Directory.Delete(cslCacheFolder, true); } catch { } }

                    // 2. Chèn tự động Link API hiện tại, kẹp thêm biến Thời gian (Ticks) để vĩnh viễn không bị lỗi Cache
                    string cacheBusterUrl = $"http://{Env.GetString("SERVER_API_IP")}:{Env.GetString("SERVER_API_PORT")}/skins/{{USERNAME}}.png?t={DateTime.Now.Ticks}";
                    
                    // 3. Viết file JSON bằng String Interpolation của C#
                    string cslConfig = $@"
                    {{
                      ""version"": ""15.01"",
                      ""loadlist"": [
                        {{ ""name"": ""OtonashiRei_LocalServer"", ""type"": ""Legacy"", ""skin"": ""{cacheBusterUrl}"", ""checkPNG"": true }},
                        {{ ""name"": ""Mojang"", ""type"": ""MojangAPI"" }}
                      ],
                      ""enableDynamicSkull"": true, 
                      ""enableTransparentSkin"": true, 
                      ""ignoreHttpsCertificate"": true
                    }}";

                    File.WriteAllText(Path.Combine(cslFolder, "CustomSkinLoader.json"), cslConfig);
                }
                catch { }

                Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_StartGame"); });
                await Task.Delay(500);

                process.EnableRaisingEvents = true;
                process.Exited += (s, ev) =>
                {
                    Dispatcher.Invoke(() => { btnPlay.IsEnabled = true; btnPlay.Content = GetLang("btn_Checking"); CheckInstallationStatus(); this.WindowState = WindowState.Normal; });
                };

                process.Start();
                HookGameProcess(process);

                Dispatcher.Invoke(() =>
                {
                    if (progressContainer != null) progressContainer.Visibility = Visibility.Collapsed;
                    _isInstalled = true;
                    if (this.FindName("txtVersionLabel") is TextBlock lblVersion) lblVersion.Text = GetLang("lbl_CurrentVersion");

                    btnPlay.Content = GetLang("btn_Playing");
                    btnPlay.IsEnabled = false;
                    NotificationManager.Show(GetLang("msg_InGame"), GetLang("msg_Connecting"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    NotificationManager.Show(GetLang("msg_SystemError"), ex.Message);
                    if (progressContainer != null) progressContainer.Visibility = Visibility.Collapsed;
                    btnPlay.IsEnabled = true;
                    CheckInstallationStatus();
                });
            }
        }

        // ================= XỬ LÝ MENU GAME (CẠNH NÚT PLAY) =================
        private void btnGameMenu_Click(object sender, RoutedEventArgs e)
        {
            if (btnGameMenu.ContextMenu != null)
            {
                btnGameMenu.ContextMenu.PlacementTarget = btnGameMenu;
                btnGameMenu.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                btnGameMenu.ContextMenu.IsOpen = true;
            }
        }

        private void menuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_minecraftDirectory))
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = _minecraftDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                NotificationManager.Show(GetLang("msg_Error"), _isEnglish ? "Game folder does not exist yet!" : "Thư mục game chưa tồn tại! Hãy bấm Khởi Động lần đầu để tạo.");
            }
        }

        private async void menuVerify_Click(object sender, RoutedEventArgs e)
        {
            if (_serverManifest == null)
            {
                NotificationManager.Show(GetLang("msg_Error"), GetLang("msg_LoadConfigError"));
                return;
            }

            btnPlay.IsEnabled = false;
            if (this.FindName("btnGameMenu") is Button btnMenu) btnMenu.IsEnabled = false;

            var progressContainer = this.FindName("DownloadProgressContainer") as FrameworkElement;
            if (progressContainer != null) progressContainer.Visibility = Visibility.Visible;
            var pBar = this.FindName("pbDownload") as ProgressBar;
            if (pBar != null) pBar.Value = 0;

            try
            {
                var path = new MinecraftPath(_minecraftDirectory);
                var launcher = new CMLauncher(path);

                launcher.FileChanged += (fileEvent) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (this.FindName("txtDownloadStatus") is TextBlock txtStat) txtStat.Text = _isEnglish ? "[Phục hồi]" : "[Restore]" + fileEvent.FileName;
                        if (this.FindName("txtDownloadDetail") is TextBlock txtDet) txtDet.Text = $"{fileEvent.ProgressedFileCount} / {fileEvent.TotalFileCount}";
                        if (pBar != null) { pBar.Maximum = fileEvent.TotalFileCount; pBar.Value = fileEvent.ProgressedFileCount; }

                        double pct = fileEvent.TotalFileCount > 0 ? (double)fileEvent.ProgressedFileCount / fileEvent.TotalFileCount * 100 : 0;
                        if (this.FindName("txtDownloadPercentage") is TextBlock txtPct) txtPct.Text = $"{pct:F0}%";
                    });
                };

                string targetVersion = GetTargetVersionName();

                if (targetVersion.StartsWith("fabric-loader"))
                {
                    string versionDir = Path.Combine(path.Versions, targetVersion);
                    string jsonPath = Path.Combine(versionDir, targetVersion + ".json");
                    if (!File.Exists(jsonPath))
                    {
                        Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_FetchFabric"); });
                        if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);
                        byte[] jsonBytes = await _httpClient.GetByteArrayAsync($"https://meta.fabricmc.net/v2/versions/loader/{_serverManifest.Version}/{_serverManifest.Loader_Version}/profile/json");
                        await File.WriteAllBytesAsync(jsonPath, jsonBytes);
                    }
                }

                Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = "Đang quét mã Hash và phục hồi file game gốc..."; });
                var versionInfo = await launcher.GetVersionAsync(targetVersion);
                await launcher.CheckAndDownloadAsync(versionInfo);

                Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = "Đang kiểm tra và đồng bộ Mods..."; });
                await SyncModsAsync(Path.Combine(path.BasePath, "mods"));

                Dispatcher.Invoke(() =>
                {
                    CheckInstallationStatus();
                    NotificationManager.Show(_isEnglish ? "SUCCESS" : "THÀNH CÔNG",
                        _isEnglish ? "All core game files and mods have been verified and restored!"
                                   : "Toàn bộ file game gốc và Mods đã được kiểm tra và phục hồi nguyên vẹn!");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_Error"), ex.Message));
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    btnPlay.IsEnabled = true;
                    if (this.FindName("btnGameMenu") is Button menu) menu.IsEnabled = true;
                    if (progressContainer != null) progressContainer.Visibility = Visibility.Collapsed;
                });
            }
        }

        private void menuUninstall_Click(object sender, RoutedEventArgs e)
        {
            string title = _isEnglish ? "UNINSTALL" : "GỠ CÀI ĐẶT";
            string desc = GetLang("msg_UninstallConfirm");
            string btnConfirmText = _isEnglish ? "CONFIRM" : "ĐỒNG Ý";
            string btnCancelText = _isEnglish ? "CANCEL" : "HỦY BỎ";

            bool confirm = NotificationManager.ShowConfirm(title, desc, btnConfirmText, btnCancelText);
            if (confirm)
            {
                try
                {
                    if (Directory.Exists(_minecraftDirectory))
                    {
                        Directory.Delete(_minecraftDirectory, true);
                        NotificationManager.Show(_isEnglish ? "SUCCESS" : "THÀNH CÔNG", GetLang("msg_UninstallSuccess"));
                    }
                }
                catch (Exception ex)
                {
                    NotificationManager.Show(GetLang("msg_Error"), ex.Message);
                }
            }
        }

        // ================= QUẢN LÝ SKIN NHÂN VẬT =================
        private ImageSource GetSkinFace(BitmapSource skinBitmap)
        {
            var baseHead = new CroppedBitmap(skinBitmap, new Int32Rect(8, 8, 8, 8));
            var hatLayer = new CroppedBitmap(skinBitmap, new Int32Rect(40, 8, 8, 8));
            var drawingGroup = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(drawingGroup, BitmapScalingMode.NearestNeighbor);
            using (var ctx = drawingGroup.Open()) { ctx.DrawImage(baseHead, new Rect(0, 0, 8, 8)); ctx.DrawImage(hatLayer, new Rect(0, 0, 8, 8)); }
            return new DrawingImage(drawingGroup);
        }

        private async Task LoadUserSkinAsync()
        {
            try
            {
                var skinData = await _httpClient.GetByteArrayAsync($"{API_SERVER_URL}/skins/{_username}.png?t={DateTime.Now.Ticks}");
                using (var ms = new MemoryStream(skinData))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = ms; bitmap.EndInit();

                    if (bitmap.PixelWidth >= 64 && bitmap.PixelHeight >= 32)
                    {
                        var faceImage = GetSkinFace(bitmap);
                        Dispatcher.Invoke(() =>
                        {
                            if (this.FindName("imgProfileAvatar") is Image imgAvatar) imgAvatar.Source = faceImage;
                            if (this.FindName("txtDefaultAvatar") is TextBlock txtDef) txtDef.Visibility = Visibility.Collapsed;
                        });
                    }
                }
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    if (this.FindName("imgProfileAvatar") is Image imgAvatar) imgAvatar.Source = null;
                    if (this.FindName("txtDefaultAvatar") is TextBlock txtDef) txtDef.Visibility = Visibility.Visible;
                });
            }
        }

        private void btnSelectSkin_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog { Filter = "PNG Image (*.png)|*.png", Title = GetLang("msg_SelectSkin") };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(openFileDialog.FileName));
                    if (bitmap.PixelWidth != 64 || (bitmap.PixelHeight != 64 && bitmap.PixelHeight != 32))
                    {
                        NotificationManager.Show(GetLang("msg_SkinError"), GetLang("msg_SkinSize"));
                        return;
                    }

                    if (this.FindName("imgSkinPreview") is Image imgPrev) imgPrev.Source = bitmap;
                    _selectedSkinBase64 = Convert.ToBase64String(File.ReadAllBytes(openFileDialog.FileName));
                    if (this.FindName("btnUploadSkin") is Button btnUpload) btnUpload.IsEnabled = true;
                }
                catch (Exception ex) { NotificationManager.Show(GetLang("msg_ReadError"), ex.Message); }
            }
        }

        private async void btnUploadSkin_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedSkinBase64)) return;

            var btnUpload = this.FindName("btnUploadSkin") as Button;
            if (btnUpload != null) { btnUpload.IsEnabled = false; btnUpload.Content = GetLang("btn_Uploading"); }

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = _username, skinBase64 = _selectedSkinBase64 }), Encoding.UTF8, "application/json");
                var responseString = await (await _httpClient.PostAsync($"{API_SERVER_URL}/auth/upload-skin", content)).Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(async () =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(GetLang("msg_Success"), GetLang("msg_SkinSuccess"));
                        _selectedSkinBase64 = "";
                        if (btnUpload != null) btnUpload.IsEnabled = false;
                        if (this.FindName("imgSkinPreview") is Image imgPrev) imgPrev.Source = null;
                        await LoadUserSkinAsync();
                    }
                    else NotificationManager.Show(GetLang("msg_Error"), result?.Message ?? GetLang("msg_SkinFail"));
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_ConnectionError"), ex.Message)); }
            finally { Dispatcher.Invoke(() => { if (btnUpload != null) btnUpload.Content = GetLang("btn_Upload"); }); }
        }

        // ================= CÁC HÀM XỬ LÝ HỒ SƠ NHÂN VẬT =================
        private async void btnSendPasswordOtp_Click(object sender, RoutedEventArgs e)
        {
            var txtOldPass = this.FindName("txtOldPassword") as PasswordBox;
            var txtNewPass = this.FindName("txtNewPassword") as PasswordBox;
            var txtConfPass = this.FindName("txtConfirmNewPassword") as PasswordBox;

            string oldPass = txtOldPass?.Password ?? ""; string newPass = txtNewPass?.Password ?? ""; string confirmPass = txtConfPass?.Password ?? "";

            if (string.IsNullOrEmpty(oldPass) || string.IsNullOrEmpty(newPass) || string.IsNullOrEmpty(confirmPass))
            {
                Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_Warning"), GetLang("msg_PassEmpty")));
                return;
            }
            if (newPass != confirmPass)
            {
                Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_Warning"), GetLang("msg_PassNotMatch")));
                return;
            }

            var btnSendOtp = this.FindName("btnSendPasswordOtp") as Button;
            if (btnSendOtp != null) { btnSendOtp.IsEnabled = false; btnSendOtp.Content = GetLang("btn_Sending"); }

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = _username, oldPassword = oldPass }), Encoding.UTF8, "application/json");
                string responseString = await (await _httpClient.PostAsync($"{API_SERVER_URL}/auth/request-password-otp", content)).Content.ReadAsStringAsync();

                if (responseString.Trim().StartsWith("<")) throw new Exception(GetLang("msg_ServerHtml"));

                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() =>
                {
                    if (btnSendOtp != null) { btnSendOtp.IsEnabled = true; btnSendOtp.Content = GetLang("btn_ChangePassword"); }

                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(GetLang("msg_Success"), GetLang("msg_OtpSent"));
                        var pnlPassIn = this.FindName("pnlPasswordInput") as FrameworkElement;
                        var pnlPassVer = this.FindName("pnlPasswordOtpVerify") as FrameworkElement;
                        if (pnlPassIn != null && pnlPassVer != null) PlayTransitionAnimation(pnlPassVer, pnlPassIn);
                    }
                    else NotificationManager.Show(GetLang("msg_Error"), result?.Message ?? GetLang("msg_WrongPass"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    NotificationManager.Show(GetLang("msg_ConnectionError"), ex.Message);
                    if (btnSendOtp != null) { btnSendOtp.IsEnabled = true; btnSendOtp.Content = GetLang("btn_ChangePassword"); }
                });
            }
        }

        private void btnCancelPasswordOtp_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("txtOldPassword") is PasswordBox t1) t1.Password = "";
            if (this.FindName("txtNewPassword") is PasswordBox t2) t2.Password = "";
            if (this.FindName("txtConfirmNewPassword") is PasswordBox t3) t3.Password = "";
            if (this.FindName("txtPasswordOtp") is TextBox t4) t4.Text = "";

            var pnlPassIn = this.FindName("pnlPasswordInput") as FrameworkElement;
            var pnlPassVer = this.FindName("pnlPasswordOtpVerify") as FrameworkElement;
            if (pnlPassIn != null && pnlPassVer != null) PlayTransitionAnimation(pnlPassIn, pnlPassVer);
        }

        private async void btnConfirmPasswordChange_Click(object sender, RoutedEventArgs e)
        {
            var txtNewPass = this.FindName("txtNewPassword") as PasswordBox;
            var txtPassOtp = this.FindName("txtPasswordOtp") as TextBox;

            string newPass = txtNewPass?.Password ?? ""; string otp = txtPassOtp?.Text ?? "";

            if (string.IsNullOrEmpty(otp)) { Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_Warning"), GetLang("msg_EnterOtp"))); return; }

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = _username, otp = otp, newPassword = newPass }), Encoding.UTF8, "application/json");
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(await (await _httpClient.PostAsync($"{API_SERVER_URL}/auth/reset-password", content)).Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(GetLang("msg_Success"), GetLang("msg_PassChangeSuccess"));
                        btnCancelPasswordOtp_Click(null, null);
                        if (File.Exists(SESSION_FILE))
                        {
                            try
                            {
                                var sDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SESSION_FILE));
                                if (sDict != null) { sDict["Password"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(newPass)); File.WriteAllText(SESSION_FILE, JsonSerializer.Serialize(sDict)); }
                            }
                            catch { }
                        }
                    }
                    else NotificationManager.Show(GetLang("msg_Error"), result?.Message ?? GetLang("msg_InvalidOtp"));
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_ConnectionError"), ex.Message)); }
        }

        private async void btnSendEmailOtp_Click(object sender, RoutedEventArgs e)
        {
            var txtEmail = this.FindName("txtNewEmail") as TextBox;
            string newEmail = txtEmail?.Text ?? "";

            if (string.IsNullOrEmpty(newEmail)) { Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_Warning"), GetLang("msg_EmailEmpty"))); return; }

            var btnSendEm = this.FindName("btnSendEmailOtp") as Button;
            if (btnSendEm != null) { btnSendEm.IsEnabled = false; btnSendEm.Content = GetLang("btn_Sending"); }

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = _username, newEmail = newEmail }), Encoding.UTF8, "application/json");
                string responseString = await (await _httpClient.PostAsync($"{API_SERVER_URL}/auth/request-email-change", content)).Content.ReadAsStringAsync();

                if (responseString.Trim().StartsWith("<")) throw new Exception(GetLang("msg_ServerHtml"));

                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() =>
                {
                    if (btnSendEm != null) { btnSendEm.IsEnabled = true; btnSendEm.Content = GetLang("btn_ChangeEmail"); }

                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(GetLang("msg_Success"), GetLang("msg_OtpSent"));
                        var pnlEmIn = this.FindName("pnlEmailInput") as FrameworkElement;
                        var pnlEmVer = this.FindName("pnlOtpVerify") as FrameworkElement;
                        if (pnlEmIn != null && pnlEmVer != null) PlayTransitionAnimation(pnlEmVer, pnlEmIn);
                    }
                    else NotificationManager.Show(GetLang("msg_Error"), result?.Message ?? GetLang("msg_EmailFail"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    NotificationManager.Show(GetLang("msg_ConnectionError"), ex.Message);
                    if (btnSendEm != null) { btnSendEm.IsEnabled = true; btnSendEm.Content = GetLang("btn_ChangeEmail"); }
                });
            }
        }

        private void btnCancelEmailOtp_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("txtNewEmail") is TextBox t1) t1.Text = "";
            if (this.FindName("txtEmailOtp") is TextBox t2) t2.Text = "";

            var pnlEmIn = this.FindName("pnlEmailInput") as FrameworkElement;
            var pnlEmVer = this.FindName("pnlOtpVerify") as FrameworkElement;
            if (pnlEmIn != null && pnlEmVer != null) PlayTransitionAnimation(pnlEmIn, pnlEmVer);
        }

        private async void btnConfirmEmailChange_Click(object sender, RoutedEventArgs e)
        {
            var txtEmail = this.FindName("txtNewEmail") as TextBox;
            var txtOtp = this.FindName("txtEmailOtp") as TextBox;

            string newEmail = txtEmail?.Text ?? ""; string otp = txtOtp?.Text ?? "";

            if (string.IsNullOrEmpty(newEmail) || string.IsNullOrEmpty(otp)) { Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_Warning"), GetLang("msg_EmailOtpEmpty"))); return; }

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = _username, newEmail = newEmail, otp = otp }), Encoding.UTF8, "application/json");
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(await (await _httpClient.PostAsync($"{API_SERVER_URL}/auth/change-email", content)).Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Dispatcher.Invoke(() =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(GetLang("msg_Success"), GetLang("msg_EmailChangeSuccess"));
                        btnCancelEmailOtp_Click(null, null);
                    }
                    else NotificationManager.Show(GetLang("msg_Error"), result?.Message ?? GetLang("msg_InvalidOtp"));
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => NotificationManager.Show(GetLang("msg_ConnectionError"), ex.Message)); }
        }

        // ================= XỬ LÝ ADD SHADER =================
        private void btnAddShader_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Shaderpack (*.zip)|*.zip",
                    Title = GetLang("msg_SelectShader")
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    if (string.IsNullOrEmpty(_minecraftDirectory)) LoadLauncherSettings();

                    string shaderPacksDir = Path.Combine(_minecraftDirectory, "shaderpacks");
                    if (!Directory.Exists(shaderPacksDir)) Directory.CreateDirectory(shaderPacksDir);

                    string sourceFilePath = openFileDialog.FileName;
                    string fileName = Path.GetFileName(sourceFilePath);
                    string destFilePath = Path.Combine(shaderPacksDir, fileName);

                    File.Copy(sourceFilePath, destFilePath, true);
                    NotificationManager.Show(GetLang("msg_Success"), GetLang("msg_ShaderSuccess"));
                }
            }
            catch (Exception ex) { NotificationManager.Show(GetLang("msg_Error"), ex.Message); }
        }

        // ================= SỰ KIỆN ANIMATION CHUYỂN TAB =================
        private void PlayTransitionAnimation(FrameworkElement targetPanel, params FrameworkElement[] panelsToHide)
        {
            if (targetPanel == null) return;
            foreach (var panel in panelsToHide) if (panel != null) panel.Visibility = Visibility.Collapsed;

            targetPanel.Visibility = Visibility.Visible; targetPanel.Opacity = 0;
            TranslateTransform trans = new TranslateTransform(0, 15); targetPanel.RenderTransform = trans;
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            DoubleAnimation slideUp = new DoubleAnimation(15, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            targetPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn); trans.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void TabHome_Checked(object sender, RoutedEventArgs e)
        {
            var ViewHome = this.FindName("ViewHome") as FrameworkElement;
            var ViewProfile = this.FindName("ViewProfile") as FrameworkElement;
            var ViewSettings = this.FindName("ViewSettings") as FrameworkElement;
            if (ViewHome != null) PlayTransitionAnimation(ViewHome, ViewProfile, ViewSettings);
        }

        private void TabProfile_Checked(object sender, RoutedEventArgs e)
        {
            var ViewHome = this.FindName("ViewHome") as FrameworkElement;
            var ViewProfile = this.FindName("ViewProfile") as FrameworkElement;
            var ViewSettings = this.FindName("ViewSettings") as FrameworkElement;
            if (ViewProfile != null) PlayTransitionAnimation(ViewProfile, ViewHome, ViewSettings);
        }

        private void TabSettings_Checked(object sender, RoutedEventArgs e)
        {
            var ViewHome = this.FindName("ViewHome") as FrameworkElement;
            var ViewProfile = this.FindName("ViewProfile") as FrameworkElement;
            var ViewSettings = this.FindName("ViewSettings") as FrameworkElement;
            if (ViewSettings != null) PlayTransitionAnimation(ViewSettings, ViewHome, ViewProfile);
        }

        private void SubTabInfo_Checked(object sender, RoutedEventArgs e)
        {
            var p1 = this.FindName("pnlProfileInfo") as FrameworkElement;
            var p2 = this.FindName("pnlProfileEmail") as FrameworkElement;
            var p3 = this.FindName("pnlProfilePassword") as FrameworkElement;
            var p4 = this.FindName("pnlProfileSkin") as FrameworkElement;
            if (p1 == null) return;
            PlayTransitionAnimation(p1, p2, p3, p4);
        }

        private void SubTabSkin_Checked(object sender, RoutedEventArgs e)
        {
            var p1 = this.FindName("pnlProfileInfo") as FrameworkElement;
            var p2 = this.FindName("pnlProfileEmail") as FrameworkElement;
            var p3 = this.FindName("pnlProfilePassword") as FrameworkElement;
            var p4 = this.FindName("pnlProfileSkin") as FrameworkElement;
            if (p4 == null) return;
            PlayTransitionAnimation(p4, p1, p2, p3);
        }

        private void SubTabEmail_Checked(object sender, RoutedEventArgs e)
        {
            var p1 = this.FindName("pnlProfileInfo") as FrameworkElement;
            var p2 = this.FindName("pnlProfileEmail") as FrameworkElement;
            var p3 = this.FindName("pnlProfilePassword") as FrameworkElement;
            var p4 = this.FindName("pnlProfileSkin") as FrameworkElement;
            if (p2 == null) return;
            PlayTransitionAnimation(p2, p1, p3, p4);

            if (this.FindName("pnlOtpVerify") is FrameworkElement otpPanel) otpPanel.Visibility = Visibility.Collapsed;
            if (this.FindName("pnlEmailInput") is FrameworkElement emailInputPanel) emailInputPanel.Visibility = Visibility.Visible;
            if (this.FindName("txtNewEmail") is TextBox t1) t1.Text = "";
            if (this.FindName("txtEmailOtp") is TextBox t2) t2.Text = "";
        }

        private void SubTabPassword_Checked(object sender, RoutedEventArgs e)
        {
            var p1 = this.FindName("pnlProfileInfo") as FrameworkElement;
            var p2 = this.FindName("pnlProfileEmail") as FrameworkElement;
            var p3 = this.FindName("pnlProfilePassword") as FrameworkElement;
            var p4 = this.FindName("pnlProfileSkin") as FrameworkElement;
            if (p3 == null) return;
            PlayTransitionAnimation(p3, p1, p2, p4);

            if (this.FindName("pnlPasswordOtpVerify") is FrameworkElement otpPanel) otpPanel.Visibility = Visibility.Collapsed;
            if (this.FindName("pnlPasswordInput") is FrameworkElement passPanel) passPanel.Visibility = Visibility.Visible;
            if (this.FindName("txtOldPassword") is PasswordBox t1) t1.Password = "";
            if (this.FindName("txtNewPassword") is PasswordBox t2) t2.Password = "";
            if (this.FindName("txtConfirmNewPassword") is PasswordBox t3) t3.Password = "";
            if (this.FindName("txtPasswordOtp") is TextBox t4) t4.Text = "";
        }

        private void SubSetGeneral_Checked(object sender, RoutedEventArgs e)
        {
            var p1 = this.FindName("pnlSettingsGeneral") as FrameworkElement;
            var p2 = this.FindName("pnlSettingsGraphics") as FrameworkElement;
            if (p1 == null) return;
            PlayTransitionAnimation(p1, p2);
        }

        private void SubSetGraphics_Checked(object sender, RoutedEventArgs e)
        {
            var p1 = this.FindName("pnlSettingsGeneral") as FrameworkElement;
            var p2 = this.FindName("pnlSettingsGraphics") as FrameworkElement;
            if (p2 == null) return;
            PlayTransitionAnimation(p2, p1);
        }

        // ================= HỆ THỐNG CẬP NHẬT CLIENT =================
        private async Task CheckForLauncherUpdate()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{API_SERVER_URL}/auth/launcher-version");
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (updateInfo != null && !string.IsNullOrEmpty(updateInfo.Version))
                    {
                        updateInfo.Version = updateInfo.Version.Replace(".zip", "", StringComparison.OrdinalIgnoreCase);

                        if (IsServerVersionNewer(updateInfo.Version, CURRENT_VERSION))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (this.FindName("btnUpdateClient") is Button btnUpdate)
                                {
                                    btnUpdate.Visibility = Visibility.Visible;
                                    btnUpdate.Tag = updateInfo;
                                }
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsServerVersionNewer(string serverVer, string clientVer)
        {
            if (string.IsNullOrWhiteSpace(serverVer) || string.IsNullOrWhiteSpace(clientVer)) return false;

            serverVer = serverVer.TrimStart('v', 'V').Trim();
            clientVer = clientVer.TrimStart('v', 'V').Trim();

            if (string.Equals(serverVer, clientVer, StringComparison.OrdinalIgnoreCase)) return false;

            // Tách con số lõi và hậu tố (VD: "1.0.0-hotfix" -> Lõi: "1.0.0", Hậu tố: "hotfix")
            string[] sParts = serverVer.Split(new char[] { '-' }, 2);
            string[] cParts = clientVer.Split(new char[] { '-' }, 2);

            if (Version.TryParse(sParts[0], out Version sVer) && Version.TryParse(cParts[0], out Version cVer))
            {
                // THỦ THUẬT KHỬ "-1": Ép toàn bộ các mốc trống về số 0 thuần túy (VD: 1.0 -> 1.0.0.0)
                var sNorm = new Version(sVer.Major, sVer.Minor, Math.Max(0, sVer.Build), Math.Max(0, sVer.Revision));
                var cNorm = new Version(cVer.Major, cVer.Minor, Math.Max(0, cVer.Build), Math.Max(0, cVer.Revision));

                if (sNorm > cNorm) return true;  // Ví dụ: 1.0.1 > 1.0.0-hotfix -> Bật Update
                if (sNorm < cNorm) return false; // Ví dụ: 1.0.0 < 1.0.1 -> Không Update

                // NẾU SỐ LÕI BẰNG NHAU (VD: cùng là mốc 1.0.0) -> So sánh hậu tố chữ phía sau
                string sSuffix = sParts.Length > 1 ? sParts[1] : "";
                string cSuffix = cParts.Length > 1 ? cParts[1] : "";

                // Bản Client chưa có hotfix, Server đã up bản hotfix -> Bật Update
                if (sSuffix != "" && cSuffix == "") return true;

                // Bản Client đang chạy hotfix, Server lại trả về bản gốc -> Không Update
                if (sSuffix == "" && cSuffix != "") return false;

                // Cùng là hotfix (VD: hotfix1 vs hotfix2) -> So sánh chuỗi tự nhiên
                return string.Compare(sSuffix, cSuffix, StringComparison.OrdinalIgnoreCase) > 0;
            }

            return serverVer != clientVer;
        }

        private void btnUpdateClient_Click(object sender, RoutedEventArgs e)
        {
            var updateInfo = (sender as Button)?.Tag as UpdateInfo;
            if (updateInfo == null || string.IsNullOrEmpty(updateInfo.DownloadUrl)) return;

            string title = GetLang("msg_UpdateAvailableTitle");
            string desc = string.Format(GetLang("msg_UpdateAvailableDesc"), updateInfo.Version);

            if (NotificationManager.ShowConfirm(title, desc))
            {
                // Mở cửa sổ Updater chuyên dụng và tắt màn hình chính
                UpdateWindow updater = new UpdateWindow(updateInfo.DownloadUrl);
                updater.Show();
                this.Close();
            }
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { this.DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }

        // ================= HÀM XỬ LÝ TẮT HOÀN TOÀN (CÓ CẢNH BÁO) =================
        private void ExitApplication()
        {
            // CẢM BIẾN: Kiểm tra xem Game có đang chạy không
            if (_activeGamePid != -1)
            {
                // Gọi đa ngôn ngữ từ thư viện thay vì set cứng
                string title = GetLang("msg_ConfirmExitTitle");
                string desc = GetLang("msg_ConfirmExitDesc");
                string btnOk = GetLang("btn_Confirm");
                string btnCancel = GetLang("btn_Cancel");

                // Bung bảng hỏi xác nhận
                bool confirm = NotificationManager.ShowConfirm(title, desc, btnOk, btnCancel);
                
                // Nếu người dùng bấm Hủy -> Trả lại Launcher, không tắt nữa
                if (!confirm) return; 
                
                // Nếu đồng ý thoát -> Bắn bỏ tiến trình Game
                try { Process.GetProcessById(_activeGamePid)?.Kill(); } catch { }
            }

            // Dọn dẹp tài nguyên và Tắt ứng dụng
            _autoSyncTimer?.Stop();
            if (_notifyIcon != null) _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appSettings.CloseMode == 2)
            {
                // LỰA CHỌN 2: Ẩn Launcher xuống khay hệ thống (Tray)
                HideToTray();
            }
            else
            {
                // LỰA CHỌN 1: Kích hoạt quy trình tắt hoàn toàn
                ExitApplication();
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSyncTimer?.Stop();
            if (_notifyIcon != null) _notifyIcon.Dispose();

            if (File.Exists(SESSION_FILE)) File.Delete(SESSION_FILE);

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

        // ================= HỆ THỐNG SYSTEM TRAY & HOOK GAME =================
        private void InitializeNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "OtonashiRei MC Server";

            try {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            } catch { }

            if (_notifyIcon.Icon == null && Uri.TryCreate("pack://application:,,,/Assets/icon.ico", UriKind.Absolute, out Uri iconUri))
            {
                try {
                    var stream = Application.GetResourceStream(iconUri)?.Stream;
                    if (stream != null) _notifyIcon.Icon = new System.Drawing.Icon(stream);
                } catch { }
            }
            if (_notifyIcon.Icon == null) _notifyIcon.Icon = System.Drawing.SystemIcons.Application;

            // XỬ LÝ SỰ KIỆN CHUỘT LÊN ICON KHAY HỆ THỐNG
            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    // Click trái: Hiện lại Launcher
                    RestoreFromTray();
                }
                else if (e.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    // Click phải: Gọi cái ContextMenu tuyệt đẹp từ XAML ra
                    ContextMenu trayMenu = (ContextMenu)this.FindResource("TrayContextMenu");
                    trayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    trayMenu.IsOpen = true;

                    // Ép Windows focus vào Menu này để khi click ra ngoài màn hình nó sẽ tự đóng
                    var hwndSource = PresentationSource.FromVisual(trayMenu) as System.Windows.Interop.HwndSource;
                    if (hwndSource != null) SetForegroundWindow(hwndSource.Handle);
                }
            };
        }

        private void HideToTray(bool showNotification = true)
        {
            if (_notifyIcon == null) InitializeNotifyIcon();
            _notifyIcon.Visible = true;
            this.Hide(); 
            
            if (showNotification) 
                NotificationManager.Show(GetLang("msg_TrayNotifyTitle"), GetLang("msg_TrayNotifyDesc"));
        }

        public void RestoreFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            if (_notifyIcon != null) _notifyIcon.Visible = false;
        }

        private void HookGameProcess(Process proc)
        {
            _activeGamePid = proc.Id;
            proc.EnableRaisingEvents = true;

            // Khi game tắt -> Tự động khôi phục lại Launcher từ khay hệ thống
            proc.Exited += (s, ev) =>
            {
                _activeGamePid = -1;
                Dispatcher.Invoke(() => {
                    btnPlay.IsEnabled = true;
                    btnPlay.Content = GetLang("btn_Checking");
                    CheckInstallationStatus();
                    RestoreFromTray(); 
                });
            };

            Dispatcher.Invoke(() => {
                btnPlay.IsEnabled = false;
                btnPlay.Content = GetLang("btn_Playing");
                
                // GỠ BỎ ĐIỀU KIỆN IF: Luôn luôn ép Launcher ẩn xuống khay khi vào game
                // Truyền 'false' để không bị chèn ép thông báo "VÀO GAME"
                HideToTray(false); 
            });
        }

        private void ScanAndHookRunningGame()
        {
            try {
                var candidates = Process.GetProcessesByName("javaw").Concat(Process.GetProcessesByName("java"));
                string normMcDir = Path.GetFullPath(_minecraftDirectory).TrimEnd('\\', '/');

                foreach (var proc in candidates) {
                    bool isOurGame = false;
                    try {
                        if (proc.MainModule != null && Path.GetFullPath(proc.MainModule.FileName).StartsWith(normMcDir, StringComparison.OrdinalIgnoreCase))
                            isOurGame = true;
                    } catch { }

                    if (!isOurGame && !string.IsNullOrEmpty(proc.MainWindowTitle) && proc.MainWindowTitle.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
                        isOurGame = true;

                    if (isOurGame) { HookGameProcess(proc); break; }
                }
            } catch { }
        }

        private void CloseOption_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsLoaded) return;
            if (sender is RadioButton rad && int.TryParse(rad.Tag?.ToString(), out int mode))
            {
                _appSettings.CloseMode = mode;
                SaveLauncherSettings();
            }
        }

        // ================= 2 HÀM SỰ KIỆN CỦA TRAY MENU XAML =================
        private void TrayMenuRestore_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayMenuExit_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }
    }
    public class ModFileInfo
    {
        public string Name { get; set; }
        public string Hash { get; set; }
    }
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
        public List<ModFileInfo>? Mods { get; set; } 
    }
    public class ModStatusItem
    {
        public string FileName { get; set; }
        public bool IsInstalled { get; set; }
        public string StatusIcon => IsInstalled ? "✔" : "❌";
        public string StatusColor => IsInstalled ? "#4ADE80" : "#FF4D4D";
    }
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
    }
    public class LauncherSettings
    {
        public int AllocatedRam { get; set; } = 8192;
        public string Language { get; set; } = "EN";
        public string InstallPath { get; set; } = "";

        // THÊM DÒNG NÀY (1 = Tắt Launcher + Tắt Game | 2 = Ẩn xuống Tray)
        public int CloseMode { get; set; } = 2; 
    }
}
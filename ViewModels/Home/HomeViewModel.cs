using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using System.Reflection;
using DotNetEnv;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinecraftLauncher.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private const string CURRENT_VERSION = "1.0.3-hotfix";
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _appDataFolder;
        private readonly string _settingsFile;
        private readonly string _sessionFile;
        private string _apiUrl;

        private string _username;
        private string _gameVersionText;
        private string _totalModsText;
        private string _downloadStatus;
        private string _downloadDetail;
        private string _downloadPercentage;
        private double _progressValue = 0;
        private double _maxProgressValue = 100;
        private int _allocatedRam = 8192;
        private int _closeMode = 2;
        private string _installPath;
        private bool _isProgressVisible;
        private bool _isPlayEnabled;

        // GÁN GIÁ TRỊ MẶC ĐỊNH ĐỂ NÚT PLAY KHÔNG BỊ TÀNG HÌNH
        private string _playButtonContent = "ĐANG KIỂM TRA...";
        private string _versionLabelText = "Phiên bản: ";

        private DispatcherTimer _autoSyncTimer;
        private FileSystemWatcher _modsWatcher;
        private int _activeGamePid = -1;
        private Dictionary<string, string> _langDict = new Dictionary<string, string>();
        private ServerInfoResponse _serverManifest;

        private ImageSource _profileAvatar;

        public event Action<string, string, string, string> OnShowConfirmDialog;
        public event Action OnRequestCloseOrHide;
        public event Action OnLogout;

        public HomeViewModel Lang => this;
        public string this[string key]
        {
            get
            {
                if (_langDict != null && _langDict.TryGetValue(key, out string value))
                    return value.Replace("\\n", Environment.NewLine);
                return key;
            }
        }

        private bool _isGameInstalled = false;
        public bool IsGameInstalled
        {
            get => _isGameInstalled;
            set => SetProperty(ref _isGameInstalled, value);
        }

        private bool _isInstalling = false;
        private bool _isCheckingStatus = false;

        // ================= QUẢN LÝ ĐỔI MẬT KHẨU =================
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmNewPassword { get; set; }

        private string _passwordOtp;
        public string PasswordOtp { get => _passwordOtp; set => SetProperty(ref _passwordOtp, value); }


        #region Các thuộc tính Binding
        public string Username { get => _username; set => SetProperty(ref _username, value); }
        public string DisplayUsername => Username?.ToUpper();
        public string GameVersionText { get => _gameVersionText; set => SetProperty(ref _gameVersionText, value); }
        public string TotalModsText { get => _totalModsText; set => SetProperty(ref _totalModsText, value); }
        public string DownloadStatus { get => _downloadStatus; set => SetProperty(ref _downloadStatus, value); }
        public string DownloadDetail { get => _downloadDetail; set => SetProperty(ref _downloadDetail, value); }
        public string DownloadPercentage { get => _downloadPercentage; set => SetProperty(ref _downloadPercentage, value); }
        public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }
        public double MaxProgressValue { get => _maxProgressValue; set => SetProperty(ref _maxProgressValue, value); }
        public bool IsProgressVisible { get => _isProgressVisible; set => SetProperty(ref _isProgressVisible, value); }
        public bool IsPlayEnabled { get => _isPlayEnabled; set => SetProperty(ref _isPlayEnabled, value); }
        public string PlayButtonContent { get => _playButtonContent; set => SetProperty(ref _playButtonContent, value); }
        public string VersionLabelText { get => _versionLabelText; set => SetProperty(ref _versionLabelText, value); }
        public string InstallPath { get => _installPath; set => SetProperty(ref _installPath, value); }

        public ImageSource ProfileAvatar
        {
            get => _profileAvatar;
            set => SetProperty(ref _profileAvatar, value);
        }

        private string _selectedSkinBase64 = "";

        private ImageSource _skinPreviewImage;
        public ImageSource SkinPreviewImage { get => _skinPreviewImage; set => SetProperty(ref _skinPreviewImage, value); }

        private bool _isUploadSkinEnabled = false;
        public bool IsUploadSkinEnabled { get => _isUploadSkinEnabled; set => SetProperty(ref _isUploadSkinEnabled, value); }

        public int AllocatedRam
        {
            get => _allocatedRam;
            set { if (SetProperty(ref _allocatedRam, value)) SaveSettingItem("AllocatedRam", value); }
        }

        public int CloseMode
        {
            get => _closeMode;
            set
            {
                if (SetProperty(ref _closeMode, value))
                {
                    SaveSettingItem("CloseMode", value);
                    // Thông báo cho giao diện biết nút nào đang được chọn
                    OnPropertyChanged(nameof(IsCloseMode1));
                    OnPropertyChanged(nameof(IsCloseMode2));
                }
            }
        }

        // Tự động nhận diện nút 1 (Thoát hẳn)
        public bool IsCloseMode1
        {
            get => CloseMode == 1;
            set { if (value) CloseMode = 1; }
        }

        // Tự động nhận diện nút 2 (Thu nhỏ xuống khay)
        public bool IsCloseMode2
        {
            get => CloseMode == 2;
            set { if (value) CloseMode = 2; }
        }

        // Sự kiện thông báo cho View ẩn xuống System Tray độc lập
        public event Action OnRequestHideToTray;

        private bool _isFullscreen = false;
        private bool _isMaximize = false;

        public bool IsFullscreen
        {
            get => _isFullscreen;
            set { if (SetProperty(ref _isFullscreen, value)) SaveSettingItem("IsFullscreen", value); }
        }

        public bool IsMaximize
        {
            get => _isMaximize;
            set { if (SetProperty(ref _isMaximize, value)) SaveSettingItem("IsMaximize", value); }
        }

        public ObservableCollection<ModStatusItem> ModsList { get; } = new ObservableCollection<ModStatusItem>();

        private bool _isUpdateAvailable = false;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        // Lưu trữ thông tin bản cập nhật để truyền cho UpdateWindow
        private string _updateDownloadUrl = "";
        private string _newVersionName = "";

        private string _newEmail;
        public string NewEmail { get => _newEmail; set => SetProperty(ref _newEmail, value); }

        private string _emailOtp;
        public string EmailOtp { get => _emailOtp; set => SetProperty(ref _emailOtp, value); }

        #endregion

        #region Commands
        public ICommand PlayCommand { get; }
        public ICommand ChangePathCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand AddShaderCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand VerifyFilesCommand { get; }
        public ICommand UninstallGameCommand { get; }
        public ICommand UpdateClientCommand { get; }
        public ICommand SendEmailOtpCommand { get; }
        public ICommand ConfirmEmailChangeCommand { get; }
        public ICommand CancelEmailOtpCommand { get; }
        public ICommand SendPasswordOtpCommand { get; }
        public ICommand ConfirmPasswordChangeCommand { get; }
        public ICommand CancelPasswordOtpCommand { get; }
        public ICommand SelectSkinCommand { get; }
        public ICommand UploadSkinCommand { get; }
        #endregion

        #region Events
        public event Action RequestShowEmailOtpPanel;
        public event Action RequestHideEmailOtpPanel;
        public event Action RequestShowPasswordOtpPanel;
        public event Action RequestHidePasswordOtpPanel;
        #endregion
        public HomeViewModel(string username)
        {
            Username = username;

            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher");
            _settingsFile = Path.Combine(_appDataFolder, "launcher_settings.json");
            _sessionFile = Path.Combine(_appDataFolder, "session_data.json");

            InitializeEnvironment();

            PlayCommand = new RelayCommand(async (p) => await ExecutePlay());
            ChangePathCommand = new RelayCommand((p) => ExecuteChangePath());
            ChangeLanguageCommand = new RelayCommand((p) => ExecuteChangeLanguage());
            LogoutCommand = new RelayCommand((p) => ExecuteLogout());
            AddShaderCommand = new RelayCommand((p) => ExecuteAddShader());
            OpenFolderCommand = new RelayCommand((p) => ExecuteOpenFolder());
            VerifyFilesCommand = new RelayCommand(async (p) => await ExecuteVerifyFiles());
            UninstallGameCommand = new RelayCommand((p) => ExecuteUninstallGame());
            UpdateClientCommand = new RelayCommand((p) => ExecuteUpdateClient());
            SendEmailOtpCommand = new RelayCommand((p) => ExecuteSendEmailOtp());
            ConfirmEmailChangeCommand = new RelayCommand((p) => ExecuteConfirmEmailChange());
            CancelEmailOtpCommand = new RelayCommand((p) =>
            {
                NewEmail = "";
                EmailOtp = "";
                RequestHideEmailOtpPanel?.Invoke();
            });
            SendPasswordOtpCommand = new RelayCommand((p) => ExecuteSendPasswordOtp());
            ConfirmPasswordChangeCommand = new RelayCommand((p) => ExecuteConfirmPasswordChange());
            CancelPasswordOtpCommand = new RelayCommand((p) =>
            {
                PasswordOtp = "";
                RequestHidePasswordOtpPanel?.Invoke();
            });
            SelectSkinCommand = new RelayCommand((p) => ExecuteSelectSkin());
            UploadSkinCommand = new RelayCommand((p) => ExecuteUploadSkin());

            LoadLauncherSettings();
            InitializeAutoSync();
        }

        // 1. HÀM GỌI API KIỂM TRA PHIÊN BẢN 
        private async Task CheckForLauncherUpdate()
        {
            try
            {
                // Gọi đúng API Endpoint của bạn
                HttpResponseMessage response = await _httpClient.GetAsync($"{_apiUrl}/auth/launcher-version");
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (updateInfo != null && !string.IsNullOrEmpty(updateInfo.Version))
                    {
                        updateInfo.Version = updateInfo.Version.Replace(".zip", "", StringComparison.OrdinalIgnoreCase);

                        if (IsServerVersionNewer(updateInfo.Version, CURRENT_VERSION))
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                // Bật cờ cho nút Update hiện lên
                                IsUpdateAvailable = true;
                                _updateDownloadUrl = updateInfo.DownloadUrl;
                                _newVersionName = updateInfo.Version;
                            });
                        }
                    }
                }
            }
            catch { }
        }

        // 2. THUẬT TOÁN ĐỌ HASH & HOTFIX XỊN XÒ CỦA BẠN (Giữ nguyên 100%)
        private bool IsServerVersionNewer(string serverVer, string clientVer)
        {
            if (string.IsNullOrWhiteSpace(serverVer) || string.IsNullOrWhiteSpace(clientVer)) return false;

            serverVer = serverVer.TrimStart('v', 'V').Trim();
            clientVer = clientVer.TrimStart('v', 'V').Trim();

            if (string.Equals(serverVer, clientVer, StringComparison.OrdinalIgnoreCase)) return false;

            string[] sParts = serverVer.Split(new char[] { '-' }, 2);
            string[] cParts = clientVer.Split(new char[] { '-' }, 2);

            if (Version.TryParse(sParts[0], out Version sVer) && Version.TryParse(cParts[0], out Version cVer))
            {
                var sNorm = new Version(sVer.Major, sVer.Minor, Math.Max(0, sVer.Build), Math.Max(0, sVer.Revision));
                var cNorm = new Version(cVer.Major, cVer.Minor, Math.Max(0, cVer.Build), Math.Max(0, cVer.Revision));

                if (sNorm > cNorm) return true;
                if (sNorm < cNorm) return false;

                string sSuffix = sParts.Length > 1 ? sParts[1] : "";
                string cSuffix = cParts.Length > 1 ? cParts[1] : "";

                if (sSuffix != "" && cSuffix == "") return true;
                if (sSuffix == "" && cSuffix != "") return false;

                return string.Compare(sSuffix, cSuffix, StringComparison.OrdinalIgnoreCase) > 0;
            }
            return serverVer != clientVer;
        }

        // 3. HÀNH ĐỘNG KHI NGƯỜI CHƠI BẤM NÚT CẬP NHẬT
        private void ExecuteUpdateClient()
        {
            if (string.IsNullOrEmpty(_updateDownloadUrl)) return;

            string title = this["msgUpdateAvailableTitle"];
            string desc = string.Format(this["msgUpdateAvailableDesc"], _newVersionName);
            string confirm = this["btnConfirm"];
            string cancel = this["btnCancel"];

            if (NotificationManager.ShowConfirm(title, desc, confirm, cancel))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Mở cửa sổ Updater chuyên dụng của bạn và tắt sảnh chính
                    var updater = new UpdateWindow(_updateDownloadUrl);
                    updater.Show();

                    if (Application.Current.MainWindow != null)
                        Application.Current.MainWindow.Close();
                });
            }
        }

        // ĐỌC THỦ CÔNG .ENV ĐỂ ĐẢM BẢO KHÔNG BAO GIỜ MẤT KẾT NỐI API
        private void InitializeEnvironment()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("MinecraftLauncher.default.env"))
            {
                if (stream != null)
                {
                    Env.Load(stream);
                }
            }

            string ip = Env.GetString("SERVER_API_IP");
            string port = Env.GetString("SERVER_API_PORT");

            _apiUrl = $"http://{ip}:{port}";
        }

        private bool IsMinecraftFullscreenEnabled()
        {
            try
            {
                // File options.txt nằm ngay trong thư mục gốc của bộ cài đặt Minecraft
                string optionsPath = Path.Combine(InstallPath, "options.txt");
                if (File.Exists(optionsPath))
                {
                    string[] lines = File.ReadAllLines(optionsPath);
                    foreach (string line in lines)
                    {
                        // Tìm dòng cấu hình thuộc tính fullscreen
                        if (line.StartsWith("fullscreen:", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = line.Split(':')[1].Trim();
                            return bool.TryParse(value, out bool fs) && fs;
                        }
                    }
                }
            }
            catch { return false; }
            // Mặc định trả về false nếu không tìm thấy file hoặc người chơi đang tắt Fullscreen
            return false;
        }

        private void LoadLauncherSettings()
        {
            bool isEnglish = true;
            string rawPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft");

            if (File.Exists(_settingsFile))
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_settingsFile));
                    if (settings != null)
                    {
                        if (settings.TryGetValue("Language", out JsonElement lang)) isEnglish = lang.GetString() == "EN";
                        if (settings.TryGetValue("AllocatedRam", out JsonElement ram)) _allocatedRam = ram.GetInt32() <= 0 ? 8192 : ram.GetInt32();
                        if (settings.TryGetValue("CloseMode", out JsonElement mode)) _closeMode = mode.GetInt32();
                        if (settings.TryGetValue("InstallPath", out JsonElement path)) rawPath = path.GetString();

                        // PHỤC HỒI ĐỌC CẤU HÌNH ĐỒ HỌA
                        if (settings.TryGetValue("IsFullscreen", out JsonElement fs)) _isFullscreen = fs.GetBoolean();
                        if (settings.TryGetValue("IsMaximize", out JsonElement mx)) _isMaximize = mx.GetBoolean();
                    }
                }
                catch { _allocatedRam = 8192; _closeMode = 2; }
            }
            else { _allocatedRam = 8192; _closeMode = 2; }

            OnPropertyChanged(nameof(AllocatedRam));
            OnPropertyChanged(nameof(CloseMode));
            OnPropertyChanged(nameof(IsFullscreen));
            OnPropertyChanged(nameof(IsMaximize));

            InstallPath = EnsureMinecraftDirectory(rawPath);
            LoadLanguagePack(isEnglish);
        }

        private void LoadLanguagePack(bool isEnglish)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string langFile = Path.Combine(baseDir, isEnglish ? "lang/en.pak" : "lang/vi.pak");
            try
            {
                if (File.Exists(langFile))
                {
                    _langDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(langFile), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    });
                }
            }
            catch { }

            OnPropertyChanged(nameof(Lang));
            OnPropertyChanged("Item[]");

            // Cập nhật lại các biến mặc định sau khi đã nạp ngôn ngữ
            VersionLabelText = this["lblVersionToDownload"];
            PlayButtonContent = this["btnChecking"];
            Task.Run(async () =>
            {
                await LoadServerInfoFromManifest();
                await LoadAvatarAsync();
                await CheckForLauncherUpdate();
                ScanAndHookRunningGame();
            });
        }

        private void SaveSettingItem(string key, object value)
        {
            try
            {
                Dictionary<string, object> currentSettings = new Dictionary<string, object>();
                if (File.Exists(_settingsFile))
                    currentSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(_settingsFile)) ?? new Dictionary<string, object>();

                currentSettings[key] = value;
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(currentSettings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private string EnsureMinecraftDirectory(string path)
        {
            if (!path.EndsWith("Minecraft", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "Minecraft");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return path;
        }

        private void InitializeAutoSync()
        {
            _autoSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _autoSyncTimer.Tick += async (s, e) => await AutoCheckServerUpdates();
            _autoSyncTimer.Start();
        }

        public async Task LoadServerInfoFromManifest()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_apiUrl}/auth/server-info");
                response.EnsureSuccessStatusCode();

                string responseString = await response.Content.ReadAsStringAsync();

                if (responseString.Trim().StartsWith("<")) throw new Exception("Node.js Server is returning HTML! Check Express.");

                _serverManifest = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                });

                if (_serverManifest != null && _serverManifest.Success)
                {
                    GameVersionText = $"{_serverManifest.Loader} {_serverManifest.Version}";
                    TotalModsText = string.Format(this["msgCheckedSync"], _serverManifest.TotalMods);
                    CheckInstallationStatus();
                    return;
                }
                throw new Exception("Success = false");
            }
            catch (Exception ex)
            {
                // Ghi Log ngầm hoặc bỏ qua MessageBox để UI không bị gián đoạn nếu rớt mạng chớp nhoáng
                Debug.WriteLine($"Lỗi LoadServerInfoFromManifest: {ex.Message}");
            }
        }

        private async Task LoadAvatarAsync()
        {
            try
            {
                // Lấy file skin gốc từ Node.js (Thêm đuôi DateTime để phá Cache)
                string skinUrl = $"{_apiUrl}/skins/{Username}.png?t={DateTime.Now.Ticks}";
                byte[] imageData = await _httpClient.GetByteArrayAsync(skinUrl);

                using (var ms = new MemoryStream(imageData))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // Đảm bảo ảnh tải về đủ kích thước chuẩn của Skin Minecraft
                    if (bitmap.PixelWidth >= 64 && bitmap.PixelHeight >= 32)
                    {
                        // 1. Cắt lớp mặt gốc (Base Head) ở tọa độ X=8, Y=8
                        var baseHead = new CroppedBitmap(bitmap, new Int32Rect(8, 8, 8, 8));
                        baseHead.Freeze();

                        // 2. Cắt lớp phụ kiện/mũ (Hat/Helmet Layer) ở tọa độ X=40, Y=8
                        var hatLayer = new CroppedBitmap(bitmap, new Int32Rect(40, 8, 8, 8));
                        hatLayer.Freeze();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // 3. Tạo một bảng vẽ để gộp 2 lớp đè lên nhau
                            var drawingGroup = new DrawingGroup();

                            // Rất quan trọng: Tắt khử răng cưa để Pixel vuông vức sắc nét giống hệt trong game
                            RenderOptions.SetBitmapScalingMode(drawingGroup, BitmapScalingMode.NearestNeighbor);

                            using (var ctx = drawingGroup.Open())
                            {
                                // Vẽ lớp mặt gốc ở dưới cùng
                                ctx.DrawImage(baseHead, new Rect(0, 0, 8, 8));
                                // Vẽ lớp kính/mũ đè lên trên (phần trong suốt sẽ nhìn xuyên thấu xuống dưới)
                                ctx.DrawImage(hatLayer, new Rect(0, 0, 8, 8));
                            }

                            // Đóng gói thành ImageSource và đưa lên Giao diện
                            var finalAvatar = new DrawingImage(drawingGroup);
                            finalAvatar.Freeze();
                            ProfileAvatar = finalAvatar;
                        });
                    }
                }
            }
            catch
            {
                // Nếu người chơi chưa tải Skin lên (Lỗi 404), trả về Null để hiện icon tay cầm mặc định
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ProfileAvatar = null;
                });
            }
        }

        private async Task AutoCheckServerUpdates()
        {
            if (!IsPlayEnabled || _serverManifest == null) return;
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_apiUrl}/auth/server-info");
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    var newManifest = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    });

                    if (newManifest != null && newManifest.Success)
                    {
                        bool isChanged = false;

                        // FIX: Khôi phục lại logic đọ Hash thay vì chỉ đếm số lượng Mod
                        if (_serverManifest.TotalMods != newManifest.TotalMods)
                        {
                            isChanged = true;
                        }
                        else if (_serverManifest.Mods != null && newManifest.Mods != null)
                        {
                            var oldHashes = _serverManifest.Mods.Select(m => m.Hash).OrderBy(h => h).ToList();
                            var newHashes = newManifest.Mods.Select(m => m.Hash).OrderBy(h => h).ToList();
                            if (!oldHashes.SequenceEqual(newHashes)) isChanged = true;
                        }

                        if (isChanged)
                        {
                            _serverManifest = newManifest;
                            GameVersionText = $"{_serverManifest.Loader} {_serverManifest.Version}";
                            TotalModsText = string.Format(this["msgCheckedSync"], _serverManifest.TotalMods);
                            CheckInstallationStatus();
                        }
                    }
                }
            }
            catch { }
        }

        private async void CheckInstallationStatus()
        {
            if (_serverManifest == null || string.IsNullOrEmpty(_serverManifest.Version)) return;

            // 🟢 CHỐNG SPAM: Nếu đang quét dở dang thì bỏ qua lệnh quét mới
            if (_isCheckingStatus) return;

            _isCheckingStatus = true;

            try
            {
                var path = new MinecraftPath(InstallPath);
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
                        // 🟢 BỨC TƯỜNG THÉP: OS có buffer ngầm gọi tới cũng bị đá văng ra nếu đang Install
                        if (_isInstalling) return;
                        Application.Current.Dispatcher.InvokeAsync(() => CheckInstallationStatus());
                    };

                    _modsWatcher.Created += onModChanged;
                    _modsWatcher.Deleted += onModChanged;
                    _modsWatcher.Renamed += new RenamedEventHandler(onModChanged);
                    _modsWatcher.EnableRaisingEvents = true;
                }

                // 🟢 TIẾP TỤC ĐẨY TÁC VỤ ĐỌC ĐĨA XUỐNG LUỒNG NỀN
                var checkResult = await Task.Run(() =>
                {
                    bool isGameCoreInstalled = File.Exists(jsonFile);
                    bool hasObsoleteVersion = false;

                    if (Directory.Exists(path.Versions))
                    {
                        var obsoleteDirs = Directory.GetDirectories(path.Versions)
                            .Where(d => !Path.GetFileName(d).Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (!isGameCoreInstalled && obsoleteDirs.Count > 0) hasObsoleteVersion = true;
                    }

                    bool areModsInstalled = true;
                    var displayList = new List<ModStatusItem>();

                    if (_serverManifest.Mods != null && _serverManifest.Mods.Count > 0)
                    {
                        var localFiles = Directory.GetFiles(modsDir).Select(Path.GetFileName).ToList();
                        var serverModNames = _serverManifest.Mods.Select(m => m.Name).ToList();

                        if (serverModNames.Except(localFiles, StringComparer.OrdinalIgnoreCase).Any() ||
                            localFiles.Except(serverModNames, StringComparer.OrdinalIgnoreCase).Any())
                        {
                            areModsInstalled = false;
                        }

                        foreach (var serverMod in _serverManifest.Mods)
                        {
                            bool isInstalled = false;
                            string localFileName = localFiles.FirstOrDefault(f => string.Equals(f, serverMod.Name, StringComparison.OrdinalIgnoreCase));

                            if (!string.IsNullOrEmpty(localFileName))
                            {
                                string localFilePath = Path.Combine(modsDir, localFileName);
                                string localHash = GetFileMD5(localFilePath);
                                string serverHash = serverMod.Hash?.Trim()?.ToLowerInvariant() ?? "";

                                if (!string.IsNullOrEmpty(localHash) && localHash != "err" && localHash == serverHash)
                                {
                                    isInstalled = true;
                                }
                            }

                            if (!isInstalled) areModsInstalled = false;
                            displayList.Add(new ModStatusItem { FileName = serverMod.Name, IsInstalled = isInstalled });
                        }
                    }

                    bool fullyInstalled = isGameCoreInstalled && areModsInstalled && !hasObsoleteVersion;

                    // Trả về dữ liệu đóng gói lên luồng chính
                    return new { isGameCoreInstalled, hasObsoleteVersion, fullyInstalled, displayList };
                });

                // 🟢 CẬP NHẬT GIAO DIỆN (Đã có sẵn data, không làm giật lag UI)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ModsList.Clear();
                    foreach (var item in checkResult.displayList) ModsList.Add(item);

                    IsGameInstalled = checkResult.isGameCoreInstalled;

                    if (_isInstalling || PlayButtonContent == this["btnPlaying"]) return;

                    IsPlayEnabled = true;

                    if (checkResult.fullyInstalled)
                    {
                        PlayButtonContent = this["btnPlay"];
                        VersionLabelText = this["lblCurrentVersion"];
                    }
                    else if (checkResult.hasObsoleteVersion)
                    {
                        PlayButtonContent = this["btnUpdate"];
                        VersionLabelText = "Yêu cầu nâng cấp:";
                    }
                    else
                    {
                        PlayButtonContent = checkResult.isGameCoreInstalled ? this["btnUpdate"] : this["btnInstall"];
                        VersionLabelText = this["lblVersionToDownload"];
                    }
                });
            }
            finally
            {
                _isCheckingStatus = false;
            }
        }
        // ================= CỖ MÁY BĂM MD5 CHO FILE (CHỐNG KHÓA FILE) =================
        private string GetFileMD5(string filePath)
        {
            try
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch { return "err"; }
        }

        private string GetTargetVersionName()
        {
            if (_serverManifest == null) return "";
            if (_serverManifest.Loader?.ToLower() == "fabric")
                return $"fabric-loader-{_serverManifest.Loader_Version}-{_serverManifest.Version}";
            return _serverManifest.Version;
        }

        private async Task ExecutePlay()
        {
            if (!IsPlayEnabled) return;

            _isInstalling = true;
            IsPlayEnabled = false;
            IsProgressVisible = true;
            PlayButtonContent = IsGameInstalled ? this["btnChecking"] : this["btnDownloading"];

            try
            {
                var path = new MinecraftPath(InstallPath);
                var launcher = new CMLauncher(path);
                string targetVersion = GetTargetVersionName();

                launcher.FileChanged += (e) =>
                {
                    DownloadStatus = $"[{e.FileKind}] {e.FileName}";
                    DownloadDetail = $"{e.ProgressedFileCount} / {e.TotalFileCount}";
                    MaxProgressValue = e.TotalFileCount;
                    ProgressValue = e.ProgressedFileCount;
                    double pct = e.TotalFileCount > 0 ? (double)e.ProgressedFileCount / e.TotalFileCount * 100 : 0;
                    DownloadPercentage = $"{pct:F0}%";
                };

                if (targetVersion.StartsWith("fabric-loader"))
                {
                    string profilePath = Path.Combine(path.Versions, targetVersion, targetVersion + ".json");
                    if (!File.Exists(profilePath))
                    {
                        DownloadStatus = this["msgFetchFabric"];
                        Directory.CreateDirectory(Path.GetDirectoryName(profilePath));
                        byte[] fabricJson = await _httpClient.GetByteArrayAsync($"https://meta.fabricmc.net/v2/versions/loader/{_serverManifest.Version}/{_serverManifest.Loader_Version}/profile/json");
                        await File.WriteAllBytesAsync(profilePath, fabricJson);
                    }
                }

                DownloadStatus = this["msgInstallCore"];
                var versionInfo = await launcher.GetVersionAsync(targetVersion);
                await launcher.CheckAndDownloadAsync(versionInfo);

                DownloadStatus = this["msgSyncServer"];
                await SyncModsAsync(Path.Combine(path.BasePath, "mods"));

                Application.Current.Dispatcher.Invoke(() => CheckInstallationStatus());

                DownloadStatus = this["msgConfigSkin"];
                ConfigureSkinServer(path.BasePath);

                DownloadStatus = this["msgStartGame"];

                bool isGameFullscreen = IsMinecraftFullscreenEnabled();

                var launchOption = new MLaunchOption
                {
                    Session = MSession.CreateOfflineSession(Username),
                    MaximumRamMb = AllocatedRam,
                    MinimumRamMb = AllocatedRam,
                    JVMArguments = [
                        $"-Xms{AllocatedRam}m",
                        $"-Xmx{AllocatedRam}m",
                        "-XX:+UseG1GC", $"-Dotonashi.ws.url=ws://{Env.GetString("SERVER_API_IP")}:{Env.GetString("SERVER_API_PORT")}",
                        $"-DapiServer=http://{Env.GetString("SERVER_API_IP")}:{Env.GetString("SERVER_API_PORT")}"
                    ],

                    ServerIp = _serverManifest.Server_Ip,
                    ServerPort = _serverManifest.Server_Port,

                    FullScreen = isGameFullscreen
                };

                if (!isGameFullscreen)
                {
                    launchOption.ScreenWidth = (int)SystemParameters.PrimaryScreenWidth;
                    launchOption.ScreenHeight = (int)SystemParameters.PrimaryScreenHeight;
                }

                var process = await launcher.CreateProcessAsync(targetVersion, launchOption);
                process.Start();
                HookGameProcess(process);
            }
            catch (Exception)
            {
                IsPlayEnabled = true;
                IsProgressVisible = false;
                CheckInstallationStatus();
            }
            finally
            {
                _isInstalling = false;

                if (PlayButtonContent != this["btnPlaying"])
                {
                    IsPlayEnabled = true;
                    IsProgressVisible = false;
                    CheckInstallationStatus();
                }
            }
        }

        private async Task SyncModsAsync(string modsDirectory)
        {
            if (_serverManifest == null || _serverManifest.Mods == null || _serverManifest.Mods.Count == 0) return;
            if (!Directory.Exists(modsDirectory)) Directory.CreateDirectory(modsDirectory);

            // 🟢 TẮT HOÀN TOÀN FILE WATCHER VÀ KHÓA LUÔN CÁC SỰ KIỆN TỒN ĐỌNG
            if (_modsWatcher != null) _modsWatcher.EnableRaisingEvents = false;

            // 🟢 ĐƯA TOÀN BỘ TÁC VỤ QUÉT FILE VÀ BĂM MD5 XUỐNG LUỒNG NỀN (BACKGROUND THREAD)
            // Giao diện sẽ hoàn toàn không bị đơ giật trong lúc dò tìm 267+ Mods!
            var modsToDownload = await Task.Run(() =>
            {
                var localFiles = Directory.GetFiles(modsDirectory).Select(Path.GetFileName).ToList();
                var serverMods = _serverManifest.Mods;
                var downloadList = new List<string>();

                foreach (var localFile in localFiles)
                {
                    var serverMod = serverMods.FirstOrDefault(m => string.Equals(m.Name, localFile, StringComparison.OrdinalIgnoreCase));
                    string fullPath = Path.Combine(modsDirectory, localFile);

                    if (serverMod == null)
                    {
                        try { File.Delete(fullPath); } catch { }
                    }
                    else
                    {
                        string localHash = GetFileMD5(fullPath);
                        string serverHash = serverMod.Hash?.Trim()?.ToLowerInvariant() ?? "";

                        if (!string.IsNullOrEmpty(localHash) && localHash != "err" && localHash != serverHash)
                        {
                            try { File.Delete(fullPath); } catch { }
                            downloadList.Add(serverMod.Name);
                        }
                    }
                }

                var validLocalFiles = Directory.GetFiles(modsDirectory).Select(Path.GetFileName).ToList();
                var serverModNames = serverMods.Select(m => m.Name).ToList();

                var missingFiles = serverModNames.Except(validLocalFiles, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var missing in missingFiles)
                {
                    if (!downloadList.Contains(missing)) downloadList.Add(missing);
                }
                return downloadList;
            });

            // 🟢 BƯỚC TẢI XUỐNG: Giao diện chỉ làm đúng việc cập nhật thanh % tiến trình
            if (modsToDownload.Count > 0)
            {
                Application.Current.Dispatcher.Invoke(() => { MaxProgressValue = modsToDownload.Count; ProgressValue = 0; });

                for (int i = 0; i < modsToDownload.Count; i++)
                {
                    string modName = modsToDownload[i];
                    DownloadStatus = string.Format(this["msgDownloadingMod"], modName);
                    DownloadDetail = $"{i + 1} / {modsToDownload.Count}";
                    DownloadPercentage = $"{(double)(i + 1) / modsToDownload.Count * 100:F0}%";

                    try
                    {
                        byte[] fileData = await _httpClient.GetByteArrayAsync($"{_apiUrl}/mods/{modName}?t={DateTime.Now.Ticks}");
                        await File.WriteAllBytesAsync(Path.Combine(modsDirectory, modName), fileData);
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Không thể tải Mod: {modName}\nLỗi từ API Server: {ex.Message}", "Lỗi Tải File"));
                    }

                    ProgressValue = i + 1;
                }
            }

            // 🟢 BẬT LẠI FILE WATCHER SAU KHI XONG XUÔI 100%
            if (_modsWatcher != null) _modsWatcher.EnableRaisingEvents = true;
        }

        private void ConfigureSkinServer(string basePath)
        {
            try
            {
                string cslFolder = Path.Combine(basePath, "CustomSkinLoader");
                Directory.CreateDirectory(cslFolder);
                try { Directory.Delete(Path.Combine(cslFolder, "caches"), true); } catch { }

                string cacheBusterUrl = $"http://{Env.GetString("SERVER_API_IP")}:{Env.GetString("SERVER_API_PORT")}/skins/{{USERNAME}}.png?t={DateTime.Now.Ticks}";
                string cslConfig = $"{{\n  \"version\": \"15.01\",\n  \"loadlist\": [\n    {{ \"name\": \"OtonashiRei_LocalServer\", \"type\": \"Legacy\", \"skin\": \"{cacheBusterUrl}\", \"checkPNG\": true }},\n    {{ \"name\": \"Mojang\", \"type\": \"MojangAPI\" }}\n  ],\n  \"enableDynamicSkull\": true,\n  \"enableTransparentSkin\": true,\n  \"ignoreHttpsCertificate\": true\n}}";
                File.WriteAllText(Path.Combine(cslFolder, "CustomSkinLoader.json"), cslConfig);
            }
            catch { }
        }

        private void HookGameProcess(Process proc)
        {
            _activeGamePid = proc.Id;
            IsPlayEnabled = false;
            PlayButtonContent = this["btnPlaying"];
            IsProgressVisible = false;

            // 🟢 PHỤC HỒI: Gọi lệnh ẩn xuống khay hệ thống (System Tray) ngay khi vừa khởi động game
            OnRequestHideToTray?.Invoke();

            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) =>
            {
                _activeGamePid = -1;
                IsProgressVisible = false;
                PlayButtonContent = "";
                CheckInstallationStatus();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow is HomeWindow view) view.RestoreFromTray();
                });
            };
        }

        private void ScanAndHookRunningGame()
        {
            try
            {
                var candidates = Process.GetProcessesByName("javaw").Concat(Process.GetProcessesByName("java"));
                foreach (var proc in candidates)
                {
                    if (proc.MainWindowTitle.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
                    {
                        Application.Current.Dispatcher.Invoke(() => HookGameProcess(proc));
                        break;
                    }
                }
            }
            catch { }
        }

        private void ExecuteChangePath()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = this["msgSelectFolder"] };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                InstallPath = EnsureMinecraftDirectory(dialog.SelectedPath);
                SaveSettingItem("InstallPath", InstallPath);
                CheckInstallationStatus();
            }
        }

        private void ExecuteChangeLanguage()
        {
            OnShowConfirmDialog?.Invoke("msgRestartRequiredTitle", "msgRestartRequiredDesc", "btnConfirm", "btnCancel");
        }

        private void ExecuteLogout()
        {
            _autoSyncTimer?.Stop();
            if (_modsWatcher != null) _modsWatcher.EnableRaisingEvents = false;
            if (File.Exists(_sessionFile)) File.Delete(_sessionFile);
            OnLogout?.Invoke();
        }

        private void ExecuteOpenFolder()
        {
            if (Directory.Exists(InstallPath))
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = InstallPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                NotificationManager.Show(this["msgError"], this["msgFolderNotExist"]);
            }
        }

        private async Task ExecuteVerifyFiles()
        {
            if (_serverManifest == null)
            {
                NotificationManager.Show(this["msgError"], this["msgLoadConfigError"]);
                return;
            }

            // 1. Khóa giao diện, ép hiện thanh tiến trình
            _isInstalling = true;
            IsPlayEnabled = false;
            IsProgressVisible = true;

            try
            {
                var path = new MinecraftPath(InstallPath);
                var launcher = new CMLauncher(path);

                launcher.FileChanged += (e) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DownloadStatus = $"[{this["msgRestoring"]}] {e.FileName}";
                        DownloadDetail = $"{e.ProgressedFileCount} / {e.TotalFileCount}";
                        MaxProgressValue = e.TotalFileCount;
                        ProgressValue = e.ProgressedFileCount;
                        double pct = e.TotalFileCount > 0 ? ((double)e.ProgressedFileCount / e.TotalFileCount) * 100 : 0;
                        DownloadPercentage = $"{pct:F0}%";
                    });
                };

                string targetVersion = GetTargetVersionName();

                if (targetVersion.StartsWith("fabric-loader"))
                {
                    string versionDir = Path.Combine(path.Versions, targetVersion);
                    string jsonPath = Path.Combine(versionDir, targetVersion + ".json");
                    if (!File.Exists(jsonPath))
                    {
                        DownloadStatus = this["msgFetchFabric"];
                        if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);
                        byte[] jsonBytes = await _httpClient.GetByteArrayAsync($"https://meta.fabricmc.net/v2/versions/loader/{_serverManifest.Version}/{_serverManifest.Loader_Version}/profile/json");
                        await File.WriteAllBytesAsync(jsonPath, jsonBytes);
                    }
                }

                DownloadStatus = this["msgVerifyHash"];
                var versionInfo = await launcher.GetVersionAsync(targetVersion);
                await launcher.CheckAndDownloadAsync(versionInfo);

                DownloadStatus = this["msgVerifyMods"];
                await SyncModsAsync(Path.Combine(path.BasePath, "mods"));

                NotificationManager.Show(this["msgSuccess"], this["msgVerifySuccess"]);
            }
            catch (Exception ex)
            {
                NotificationManager.Show(this["msgError"], ex.Message);
            }
            finally
            {
                // 2. Mở khóa giao diện và quét lại nút Play
                _isInstalling = false;
                IsPlayEnabled = true;
                IsProgressVisible = false;
                CheckInstallationStatus();
            }
        }

        private void ExecuteUninstallGame()
        {
            if (NotificationManager.ShowConfirm(this["menuUninstall"], this["msgUninstallConfirm"], this["btnConfirm"], this["btnCancel"]))
            {
                try
                {
                    // 1. NGẮT KẾT NỐI: Phải tiêu diệt con mắt giám sát để nó nhả khóa thư mục ra
                    if (_modsWatcher != null)
                    {
                        _modsWatcher.EnableRaisingEvents = false;
                        _modsWatcher.Dispose();
                        _modsWatcher = null;
                    }

                    // 2. Ra lệnh quét sạch dữ liệu Game
                    if (Directory.Exists(InstallPath))
                    {
                        Directory.Delete(InstallPath, true);
                    }

                    NotificationManager.Show(this["msgSuccess"], this["msgUninstallSuccess"]);
                }
                catch (Exception ex)
                {
                    // Bắt lỗi nếu bạn đang vô tình mở thư mục Minecraft bằng File Explorer
                    NotificationManager.Show(this["msgError"], "Không thể xóa hoàn toàn: " + ex.Message);
                }
                finally
                {
                    // 3. QUAN TRỌNG NHẤT: Bắt buộc giao diện phải quét lại toàn bộ trạng thái
                    // Lúc này danh sách Mod sẽ bị xóa sạch thành dấu X đỏ và nút sẽ quay về chữ CÀI ĐẶT
                    CheckInstallationStatus();
                }
            }
        }

        private void ExecuteAddShader()
        {
            var openFileDialog = new OpenFileDialog { Filter = "Shaderpack (*.zip)|*.zip", Title = this["msgSelectShader"] };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string shaderDir = Path.Combine(InstallPath, "shaderpacks");
                    if (!Directory.Exists(shaderDir)) Directory.CreateDirectory(shaderDir);
                    File.Copy(openFileDialog.FileName, Path.Combine(shaderDir, Path.GetFileName(openFileDialog.FileName)), true);
                    NotificationManager.Show(this["msgSuccess"], this["msgShaderSuccess"]);
                }
                catch (Exception ex) { NotificationManager.Show(this["msgError"], ex.Message); }
            }
        }

        private async void ExecuteSendEmailOtp()
        {
            if (string.IsNullOrEmpty(NewEmail))
            {
                NotificationManager.Show(this["msgWarning"], this["msgEmailEmpty"]);
                return;
            }

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = Username, newEmail = NewEmail }), System.Text.Encoding.UTF8, "application/json");
                string responseString = await (await _httpClient.PostAsync($"{_apiUrl}/auth/request-email-change", content)).Content.ReadAsStringAsync();

                if (responseString.Trim().StartsWith("<")) throw new Exception(this["msgServerHtml"]);

                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(this["msgSuccess"], this["msgOtpSent"]);
                        RequestShowEmailOtpPanel?.Invoke(); // Kích hoạt animation trượt bảng OTP lên
                    }
                    else
                    {
                        // Đảm bảo dòng này dùng this[result.Message] để tự động dò Key trong từ điển
                        string errorCode = result?.Message ?? "msgEmailFail";
                        NotificationManager.Show(this["msgError"], this[errorCode]);
                    }
                });
            }
            catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => NotificationManager.Show(this["msgConnectionError"], ex.Message)); }
        }

        private async void ExecuteConfirmEmailChange()
        {
            if (string.IsNullOrEmpty(NewEmail) || string.IsNullOrEmpty(EmailOtp))
            {
                NotificationManager.Show(this["msgWarning"], this["msgEmailOtpEmpty"]);
                return;
            }

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = Username, newEmail = NewEmail, otp = EmailOtp }), System.Text.Encoding.UTF8, "application/json");
                string responseString = await (await _httpClient.PostAsync($"{_apiUrl}/auth/change-email", content)).Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(this["msgSuccess"], this["msgEmailChangeSuccess"]);
                        NewEmail = "";
                        EmailOtp = "";
                        RequestHideEmailOtpPanel?.Invoke(); // Kích hoạt animation quay lại
                    }
                    else NotificationManager.Show(this["msgError"], result?.Message ?? this["msgInvalidOtp"]);
                });
            }
            catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => NotificationManager.Show(this["msgConnectionError"], ex.Message)); }
        }

        private async void ExecuteSendPasswordOtp()
        {
            if (string.IsNullOrEmpty(OldPassword) || string.IsNullOrEmpty(NewPassword) || string.IsNullOrEmpty(ConfirmNewPassword))
            {
                NotificationManager.Show(this["msgWarning"], this["msgPassEmpty"]);
                return;
            }
            if (NewPassword != ConfirmNewPassword)
            {
                NotificationManager.Show(this["msgWarning"], this["msgPassNotMatch"]);
                return;
            }

            try
            {
                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { username = Username, oldPassword = OldPassword }), System.Text.Encoding.UTF8, "application/json");
                string responseString = await (await _httpClient.PostAsync($"{_apiUrl}/auth/request-password-otp", content)).Content.ReadAsStringAsync();

                var result = System.Text.Json.JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(this["msgSuccess"], this["msgOtpSent"]);
                        RequestShowPasswordOtpPanel?.Invoke();
                    }
                    else
                    {
                        string errorCode = result?.Message ?? "msgWrongPass";
                        NotificationManager.Show(this["msgError"], this[errorCode]);
                    }
                });
            }
            catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => NotificationManager.Show(this["msgConnectionError"], ex.Message)); }
        }

        private async void ExecuteConfirmPasswordChange()
        {
            if (string.IsNullOrEmpty(PasswordOtp))
            {
                NotificationManager.Show(this["msgWarning"], this["msgEnterOtp"]);
                return;
            }

            try
            {
                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { username = Username, otp = PasswordOtp, newPassword = NewPassword }), System.Text.Encoding.UTF8, "application/json");
                string responseString = await (await _httpClient.PostAsync($"{_apiUrl}/auth/reset-password", content)).Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(this["msgSuccess"], this["msgPassChangeSuccess"]);

                        // Cập nhật lại phiên đăng nhập (Session) tự động nếu người dùng có tick Lưu mật khẩu
                        string sessionFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "session_data.json");
                        if (System.IO.File.Exists(sessionFile))
                        {
                            try
                            {
                                var sDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(sessionFile));
                                if (sDict != null)
                                {
                                    sDict["Password"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(NewPassword));
                                    System.IO.File.WriteAllText(sessionFile, System.Text.Json.JsonSerializer.Serialize(sDict));
                                }
                            }
                            catch { }
                        }

                        PasswordOtp = "";
                        RequestHidePasswordOtpPanel?.Invoke();
                    }
                    else
                    {
                        string errorCode = result?.Message ?? "msgInvalidOtp";
                        NotificationManager.Show(this["msgError"], this[errorCode]);
                    }
                });
            }
            catch (Exception ex) { Application.Current.Dispatcher.Invoke(() => NotificationManager.Show(this["msgConnectionError"], ex.Message)); }
        }

        private void ExecuteSelectSkin()
        {
            var openFileDialog = new OpenFileDialog { Filter = "PNG Image (*.png)|*.png", Title = this["msgSelectSkin"] };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(openFileDialog.FileName));
                    if (bitmap.PixelWidth != 64 || (bitmap.PixelHeight != 64 && bitmap.PixelHeight != 32))
                    {
                        NotificationManager.Show(this["msgSkinError"], this["msgSkinSize"]);
                        return;
                    }

                    SkinPreviewImage = bitmap;
                    _selectedSkinBase64 = Convert.ToBase64String(File.ReadAllBytes(openFileDialog.FileName));
                    IsUploadSkinEnabled = true;
                }
                catch (Exception ex) { NotificationManager.Show(this["msgReadError"], ex.Message); }
            }
        }

        private async void ExecuteUploadSkin()
        {
            if (string.IsNullOrEmpty(_selectedSkinBase64)) return;
            IsUploadSkinEnabled = false;

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new { username = Username, skinBase64 = _selectedSkinBase64 }), System.Text.Encoding.UTF8, "application/json");
                var responseString = await (await _httpClient.PostAsync($"{_apiUrl}/auth/upload-skin", content)).Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ServerInfoResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Application.Current.Dispatcher.Invoke(async () =>
                {
                    if (result != null && result.Success)
                    {
                        NotificationManager.Show(this["msgSuccess"], this["msgSkinSuccess"]);
                        _selectedSkinBase64 = "";
                        SkinPreviewImage = null;

                        // 🟢 GỌI LẠI ĐÚNG HÀM CÓ SẴN CỦA BẠN ĐỂ CẬP NHẬT GIAO DIỆN
                        await LoadAvatarAsync();
                    }
                    else
                    {
                        string errorCode = result?.Message ?? "msgSkinFail";
                        NotificationManager.Show(this["msgError"], this[errorCode]);
                        IsUploadSkinEnabled = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    NotificationManager.Show(this["msgConnectionError"], ex.Message);
                    IsUploadSkinEnabled = true;
                });
            }
        }

        public void CloseOrExitRequested()
        {
            if (_activeGamePid != -1)
            {
                if (NotificationManager.ShowConfirm(this["msgConfirmExitTitle"], this["msgConfirmExitDesc"], this["btnConfirm"], this["btnCancel"]))
                    try { Process.GetProcessById(_activeGamePid)?.Kill(); } catch { }
                else return;
            }
            _autoSyncTimer?.Stop();
            Application.Current.Shutdown();
        }
    }
}
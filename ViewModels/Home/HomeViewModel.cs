using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using DotNetEnv;
using Microsoft.Win32;

namespace MinecraftLauncher.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _appDataFolder;
        private readonly string _settingsFile;
        private readonly string _sessionFile;
        private string _apiUrl;

        private string _username;
        private string _gameVersionText = "Đang tải...";
        private string _totalModsText = "Đang kết nối Máy chủ...";
        private string _downloadStatus = "...";
        private string _downloadDetail = "0 / 0";
        private string _downloadPercentage = "0%";
        private double _progressValue = 0;
        private double _maxProgressValue = 100;
        private int _allocatedRam;
        private int _closeMode = 2;
        private string _installPath;
        private bool _isProgressVisible = false;
        private bool _isPlayEnabled = false;

        // GÁN GIÁ TRỊ MẶC ĐỊNH ĐỂ NÚT PLAY KHÔNG BỊ TÀNG HÌNH
        private string _playButtonContent = "ĐANG KIỂM TRA...";
        private string _versionLabelText = "Phiên bản: ";

        private DispatcherTimer _autoSyncTimer;
        private FileSystemWatcher _modsWatcher;
        private int _activeGamePid = -1;
        private Dictionary<string, string> _langDict = new Dictionary<string, string>();
        private ServerInfoResponse _serverManifest;

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

        #region Các thuộc tính Binding
        public string Username { get => _username; set => SetProperty(ref _username, value); }
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

        public ObservableCollection<ModStatusItem> ModsList { get; } = new ObservableCollection<ModStatusItem>();
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
        #endregion

        public HomeViewModel(string username)
        {
            Username = username.ToUpper();

            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher");
            _settingsFile = Path.Combine(_appDataFolder, "launcher_settings.json");
            _sessionFile = Path.Combine(_appDataFolder, "session_data.json");

            InitializeEnvironment();

            PlayCommand = new RelayCommand(async (p) => await ExecutePlay(), (p) => IsPlayEnabled);
            ChangePathCommand = new RelayCommand((p) => ExecuteChangePath());
            ChangeLanguageCommand = new RelayCommand((p) => ExecuteChangeLanguage());
            LogoutCommand = new RelayCommand((p) => ExecuteLogout());
            AddShaderCommand = new RelayCommand((p) => ExecuteAddShader());
            OpenFolderCommand = new RelayCommand((p) => ExecuteOpenFolder());
            VerifyFilesCommand = new RelayCommand(async (p) => await ExecuteVerifyFiles());
            UninstallGameCommand = new RelayCommand((p) => ExecuteUninstallGame());

            LoadLauncherSettings();
            InitializeAutoSync();
        }

        // ĐỌC THỦ CÔNG .ENV ĐỂ ĐẢM BẢO KHÔNG BAO GIỜ MẤT KẾT NỐI API
        private void InitializeEnvironment()
        {
            string envPath = Path.Combine(_appDataFolder, ".env");

            if (!File.Exists(envPath))
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("MinecraftLauncher.default.env"))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                            File.WriteAllText(envPath, reader.ReadToEnd());
                    }
                    else File.WriteAllText(envPath, "SERVER_API_IP=127.0.0.1\nSERVER_API_PORT=3000");
                }
            }

            Env.Load(envPath);
            string ip = Env.GetString("SERVER_API_IP");
            string port = Env.GetString("SERVER_API_PORT");

            // Backup an toàn: Nếu Env lỗi, tự bóc tách file bằng tay
            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(port))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    if (line.StartsWith("SERVER_API_IP=")) ip = line.Split('=')[1].Trim();
                    if (line.StartsWith("SERVER_API_PORT=")) port = line.Split('=')[1].Trim();
                }
            }

            _apiUrl = $"http://{ip ?? "127.0.0.1"}:{port ?? "3000"}";
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
                        if (settings.TryGetValue("AllocatedRam", out JsonElement ram))
                            _allocatedRam = ram.ValueKind == JsonValueKind.Number ? ram.GetInt32() : int.Parse(ram.GetString() ?? "8192");

                        if (settings.TryGetValue("CloseMode", out JsonElement mode))
                            _closeMode = mode.ValueKind == JsonValueKind.Number ? mode.GetInt32() : int.Parse(mode.GetString() ?? "2");
                        if (settings.TryGetValue("InstallPath", out JsonElement path)) rawPath = path.GetString();
                    }
                }
                catch { _allocatedRam = 8192; _closeMode = 2; }
            }
            else { _allocatedRam = 8192; _closeMode = 2; }

            OnPropertyChanged(nameof(AllocatedRam));
            OnPropertyChanged(nameof(CloseMode));

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

                // Nếu Máy chủ Nodejs lỗi, nó sẽ trả về mã HTML thay vì JSON. Phải chặn ngay!
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
                // IN CHI TIẾT LỖI RA MÀN HÌNH ĐỂ DEBUG
                MessageBox.Show($"Lỗi: {ex.Message} | API: {_apiUrl}");
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
                        bool isChanged = _serverManifest.TotalMods != newManifest.TotalMods;
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

        public void CheckInstallationStatus()
        {
            if (_serverManifest == null || string.IsNullOrEmpty(_serverManifest.Version)) return;

            var path = new MinecraftPath(InstallPath);
            string targetVersion = GetTargetVersionName();
            string jsonFile = Path.Combine(path.Versions, targetVersion, targetVersion + ".json");
            string modsDir = Path.Combine(path.BasePath, "mods");

            if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);

            if (_modsWatcher == null)
            {
                _modsWatcher = new FileSystemWatcher(modsDir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                _modsWatcher.Created += (s, e) => CheckInstallationStatus();
                _modsWatcher.Deleted += (s, e) => CheckInstallationStatus();
                _modsWatcher.EnableRaisingEvents = true;
            }

            bool isGameCoreInstalled = File.Exists(jsonFile);
            bool areModsInstalled = true;
            var localDisplayList = new List<ModStatusItem>();

            if (_serverManifest.Mods != null)
            {
                var localFiles = Directory.GetFiles(modsDir).Select(Path.GetFileName).ToList();
                foreach (var serverMod in _serverManifest.Mods)
                {
                    bool isModOk = localFiles.Contains(serverMod.Name, StringComparer.OrdinalIgnoreCase);
                    if (!isModOk) areModsInstalled = false;
                    localDisplayList.Add(new ModStatusItem { FileName = serverMod.Name, IsInstalled = isModOk });
                }
            }

            bool fullyInstalled = isGameCoreInstalled && areModsInstalled;

            Application.Current.Dispatcher.Invoke(() =>
            {
                ModsList.Clear();
                foreach (var item in localDisplayList) ModsList.Add(item);

                if (PlayButtonContent == this["btnPlaying"]) return;

                IsPlayEnabled = true;

                if (fullyInstalled)
                {
                    PlayButtonContent = this["btnPlay"];
                    VersionLabelText = this["lblCurrentVersion"];
                }
                else
                {
                    PlayButtonContent = isGameCoreInstalled ? this["btnUpdate"] : this["btnInstall"];
                    VersionLabelText = this["lblVersionToDownload"];
                }
            });
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
            IsPlayEnabled = false;
            IsProgressVisible = true;
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

                DownloadStatus = this["msgConfigSkin"];
                ConfigureSkinServer(path.BasePath);

                DownloadStatus = this["msgStartGame"];
                var launchOption = new MLaunchOption
                {
                    Session = MSession.CreateOfflineSession(Username),
                    MaximumRamMb = AllocatedRam,
                    MinimumRamMb = AllocatedRam,
                    JVMArguments = new string[] { $"-Xms{AllocatedRam}m", $"-Xmx{AllocatedRam}m", "-XX:+UseG1GC" },
                    ServerIp = string.IsNullOrEmpty(_serverManifest.Server_Ip) ? "127.0.0.1" : _serverManifest.Server_Ip,
                    ServerPort = _serverManifest.Server_Port > 0 ? _serverManifest.Server_Port : 25565
                };

                var process = await launcher.CreateProcessAsync(targetVersion, launchOption);
                process.Start();
                HookGameProcess(process);
            }
            catch (Exception ex)
            {
                NotificationManager.Show(this["msgSystemError"], ex.Message);
                IsPlayEnabled = true;
                IsProgressVisible = false;
                CheckInstallationStatus();
            }
        }

        private async Task SyncModsAsync(string modsDirectory)
        {
            if (_serverManifest?.Mods == null) return;
            var localFiles = Directory.GetFiles(modsDirectory).Select(Path.GetFileName).ToList();

            foreach (var local in localFiles)
            {
                if (!_serverManifest.Mods.Any(m => string.Equals(m.Name, local, StringComparison.OrdinalIgnoreCase)))
                    try { File.Delete(Path.Combine(modsDirectory, local)); } catch { }
            }

            var currentFiles = Directory.GetFiles(modsDirectory).Select(Path.GetFileName).ToList();
            var missingMods = _serverManifest.Mods.Where(m => !currentFiles.Contains(m.Name, StringComparer.OrdinalIgnoreCase)).ToList();

            if (missingMods.Count > 0)
            {
                Application.Current.Dispatcher.Invoke(() => { MaxProgressValue = missingMods.Count; ProgressValue = 0; });
                for (int i = 0; i < missingMods.Count; i++)
                {
                    DownloadStatus = string.Format(this["msgDownloadingMod"], missingMods[i].Name);
                    DownloadDetail = $"{i + 1} / {missingMods.Count}";
                    DownloadPercentage = $"{(double)(i + 1) / missingMods.Count * 100:F0}%";

                    try
                    {
                        byte[] fileData = await _httpClient.GetByteArrayAsync($"{_apiUrl}/mods/{missingMods[i].Name}?t={DateTime.Now.Ticks}");
                        await File.WriteAllBytesAsync(Path.Combine(modsDirectory, missingMods[i].Name), fileData);
                    }
                    catch { }
                    ProgressValue = i + 1;
                }
            }
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

            OnRequestCloseOrHide?.Invoke();

            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) =>
            {
                _activeGamePid = -1;
                IsProgressVisible = false;
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
            if (Directory.Exists(InstallPath)) Process.Start(new ProcessStartInfo { FileName = InstallPath, UseShellExecute = true, Verb = "open" });
        }

        private async Task ExecuteVerifyFiles()
        {
            IsPlayEnabled = false;
            IsProgressVisible = true;
            try
            {
                var path = new MinecraftPath(InstallPath);
                var launcher = new CMLauncher(path);
                string targetVersion = GetTargetVersionName();
                var versionInfo = await launcher.GetVersionAsync(targetVersion);
                await launcher.CheckAndDownloadAsync(versionInfo);
                await SyncModsAsync(Path.Combine(path.BasePath, "mods"));
                NotificationManager.Show(this["msgSuccess"], this["msgVerifySuccess"]);
            }
            catch (Exception ex) { NotificationManager.Show(this["msgError"], ex.Message); }
            finally { IsProgressVisible = false; CheckInstallationStatus(); }
        }

        private void ExecuteUninstallGame()
        {
            if (NotificationManager.ShowConfirm(this["msgError"], this["msgUninstallConfirm"]))
            {
                try
                {
                    if (Directory.Exists(InstallPath))
                    {
                        Directory.Delete(InstallPath, true);
                        NotificationManager.Show(this["msgSuccess"], this["msgUninstallSuccess"]);
                        CheckInstallationStatus();
                    }
                }
                catch (Exception ex) { NotificationManager.Show(this["msgError"], ex.Message); }
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

        public void CloseOrExitRequested()
        {
            if (_activeGamePid != -1)
            {
                if (NotificationManager.ShowConfirm(this["msgConfirmExitTitle"], this["msgConfirmExitDesc"]))
                    try { Process.GetProcessById(_activeGamePid)?.Kill(); } catch { }
                else return;
            }
            _autoSyncTimer?.Stop();
            Application.Current.Shutdown();
        }
    }
}
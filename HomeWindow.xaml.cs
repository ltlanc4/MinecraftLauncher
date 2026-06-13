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
        private readonly string CURRENT_VERSION = "1.0.1";
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

        public HomeWindow(string username, string token, string uuid)
        {
            InitializeComponent();
            
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher");
            if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);

            SESSION_FILE = Path.Combine(_appDataFolder, "session_data.json");
            SETTINGS_FILE = Path.Combine(_appDataFolder, "launcher_settings.json"); // File gom chung

            // Load .env
            string envPath = Path.Combine(_appDataFolder, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
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
                Env.Load(envPath);
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

            // 4. Áp dụng Đồ họa lên nút tick
            if (this.FindName($"radGraphics{_appSettings.GraphicsPreset}") is RadioButton rad) rad.IsChecked = true;

            // Mở khóa cho phép lưu file khi có thao tác mới
            _isSettingsLoaded = true;
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

        private void GraphicsPreset_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsLoaded) return;
            if (sender is RadioButton rad && rad.Tag != null)
            {
                _appSettings.GraphicsPreset = rad.Tag.ToString();
                SaveLauncherSettings();
            }
        }

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

            bool isConfirm = NotificationManager.ShowConfirm(title, message);
            if (isConfirm)
            {
                _isEnglish = !_isEnglish;
                _appSettings.Language = _isEnglish ? "EN" : "VI";
                SaveLauncherSettings(); // LƯU VÀO JSON

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

            foreach (var kvp in _langDict)
            {
                if (kvp.Key.StartsWith("msg_") || kvp.Key.StartsWith("btn_") || kvp.Key.StartsWith("lbl_")) continue;
                var element = this.FindName(kvp.Key);
                if (element is TextBlock tb) tb.Text = kvp.Value;
                else if (element is Button btn) btn.Content = kvp.Value;
                else if (element is RadioButton rad) rad.Content = kvp.Value;
            }

            if (this.FindName("btnSettings") is Button btnSet) btnSet.ToolTip = GetLang("btnSettingsTooltip");
            var btnUpSkin = this.FindName("btnUploadSkin") as Button;
            if (btnUpSkin != null && btnUpSkin.IsEnabled) btnUpSkin.Content = GetLang("btn_Upload");

            var btnSendEm = this.FindName("btnSendEmailOtp") as Button;
            if (btnSendEm != null && btnSendEm.IsEnabled) btnSendEm.Content = GetLang("btn_ChangeEmail");

            var btnSendPw = this.FindName("btnSendPasswordOtp") as Button;
            if (btnSendPw != null && btnSendPw.IsEnabled) btnSendPw.Content = GetLang("btn_ChangePassword");

            if (this.FindName("lblEmailOtpStr") is TextBlock lblEmOtp) lblEmOtp.Text = GetLang("lbl_EmailOtpStr");
            if (this.FindName("lblPassOtpStr") is TextBlock lblPwOtp) lblPwOtp.Text = GetLang("lbl_PassOtpStr");
            if (this.FindName("lblSettingsSidebarTitle") is TextBlock lblSetSide) lblSetSide.Text = GetLang("lblSettingsSidebarTitle");
            if (this.FindName("radSubSetGeneral") is RadioButton radGen) radGen.Content = GetLang("radSubSetGeneral");
            if (this.FindName("radSubSetGraphics") is RadioButton radGra) radGra.Content = GetLang("radSubSetGraphics");
            if (this.FindName("lblSettingsGeneralTitle") is TextBlock lblGenTitle) lblGenTitle.Text = GetLang("lblSettingsGeneralTitle");
            if (this.FindName("lblSettingsGraphicsTitle") is TextBlock lblGraTitle) lblGraTitle.Text = GetLang("lblSettingsGraphicsTitle");
            if (this.FindName("lblShaderStr") is TextBlock lblShader) lblShader.Text = GetLang("lblShaderStr");
            if (this.FindName("btnAddShader") is Button btnShader) btnShader.Content = GetLang("btn_AddShader");
            if (this.FindName("btnUpdateClient") is Button btnUpdClient) btnUpdClient.Content = GetLang("btn_UpdateClient");
            if (this.FindName("menuOpenFolder") is MenuItem mnuOpen) mnuOpen.Header = GetLang("menu_OpenFolder");
            if (this.FindName("menuVerify") is MenuItem mnuVerify) mnuVerify.Header = GetLang("menu_Verify");
            if (this.FindName("menuUninstall") is MenuItem mnuUninst) mnuUninst.Header = GetLang("menu_Uninstall");
            if (this.FindName("lblRamStr") is TextBlock lblRam) lblRam.Text = GetLang("lbl_RamStr");

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
                            if (newManifest.Mods.Except(_serverManifest.Mods).Any() || _serverManifest.Mods.Except(newManifest.Mods).Any()) isChanged = true;
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
                _modsWatcher = new FileSystemWatcher(modsDir);
                _modsWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;

                FileSystemEventHandler onModChanged = (s, e) =>
                {
                    Dispatcher.Invoke(() => { if (btnPlay != null && btnPlay.IsEnabled) CheckInstallationStatus(); });
                };
                _modsWatcher.Deleted += onModChanged;
                _modsWatcher.Created += onModChanged;
                _modsWatcher.Renamed += new RenamedEventHandler(onModChanged);
                _modsWatcher.EnableRaisingEvents = true;
            }

            bool isGameInstalled = File.Exists(jsonFile);
            bool areModsInstalled = true;
            var displayList = new List<ModStatusItem>();

            if (_serverManifest.Mods != null && _serverManifest.Mods.Count > 0)
            {
                var localFiles = Directory.GetFiles(modsDir).Select(Path.GetFileName).ToList();
                if (_serverManifest.Mods.Except(localFiles).Any() || localFiles.Except(_serverManifest.Mods).Any()) areModsInstalled = false;
                foreach (var m in _serverManifest.Mods) displayList.Add(new ModStatusItem { FileName = m, IsInstalled = localFiles.Contains(m) });
            }

            _isInstalled = isGameInstalled && areModsInstalled;

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
                else if (isGameInstalled && !areModsInstalled)
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
            var serverFiles = _serverManifest.Mods;

            var filesToDelete = localFiles.Except(serverFiles).ToList();
            foreach (var file in filesToDelete) { try { File.Delete(Path.Combine(modsDirectory, file)); } catch { } }

            var filesToDownload = serverFiles.Except(localFiles).ToList();
            if (filesToDownload.Count > 0)
            {
                var pBar = this.FindName("pbDownload") as ProgressBar;
                Dispatcher.Invoke(() => { if (pBar != null) { pBar.Maximum = filesToDownload.Count; pBar.Value = 0; } });

                for (int i = 0; i < filesToDownload.Count; i++)
                {
                    string modFile = filesToDownload[i];
                    Dispatcher.Invoke(() =>
                    {
                        if (this.FindName("txtDownloadStatus") is TextBlock txtStat) txtStat.Text = string.Format(GetLang("msg_DownloadingMod"), modFile);
                        if (this.FindName("txtDownloadDetail") is TextBlock txtDet) txtDet.Text = string.Format(GetLang("msg_FileCount"), i, filesToDownload.Count);
                        if (this.FindName("txtDownloadPercentage") is TextBlock txtPct) txtPct.Text = $"{((double)i / filesToDownload.Count * 100):F0}%";
                    });

                    try
                    {
                        byte[] fileBytes = await _httpClient.GetByteArrayAsync($"{API_SERVER_URL}/mods/{modFile}");
                        await File.WriteAllBytesAsync(Path.Combine(modsDirectory, modFile), fileBytes);
                    }
                    catch (Exception) { }

                    Dispatcher.Invoke(() => { if (pBar != null) pBar.Value = i + 1; });
                }
            }
        }

        // ================= HỆ THỐNG BƠM CẤU HÌNH ĐỒ HỌA =================
        private void ApplyGraphicsPresetToMinecraft(string optionsFilePath)
        {
            var settingsToInject = new Dictionary<string, string>();

            switch (_appSettings.GraphicsPreset)
            {
                case "Low":
                    settingsToInject["renderDistance"] = "6";
                    settingsToInject["graphicsMode"] = "FAST";
                    settingsToInject["particles"] = "2"; // Minimal
                    settingsToInject["entityDistanceScaling"] = "0.5";
                    break;
                case "Medium":
                    settingsToInject["renderDistance"] = "12";
                    settingsToInject["graphicsMode"] = "FANCY";
                    settingsToInject["particles"] = "1"; // Decreased
                    settingsToInject["entityDistanceScaling"] = "1.0";
                    break;
                case "High":
                    settingsToInject["renderDistance"] = "18";
                    settingsToInject["graphicsMode"] = "FANCY";
                    settingsToInject["particles"] = "0"; // All
                    settingsToInject["entityDistanceScaling"] = "1.5";
                    break;
                case "Ultra":
                    settingsToInject["renderDistance"] = "24";
                    settingsToInject["graphicsMode"] = "FABULOUS";
                    settingsToInject["particles"] = "0";
                    settingsToInject["entityDistanceScaling"] = "2.0";
                    break;
            }

            if (File.Exists(optionsFilePath))
            {
                try
                {
                    var lines = File.ReadAllLines(optionsFilePath).ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        string[] parts = lines[i].Split(':');
                        if (parts.Length > 0 && settingsToInject.ContainsKey(parts[0]))
                        {
                            lines[i] = $"{parts[0]}:{settingsToInject[parts[0]]}";
                            settingsToInject.Remove(parts[0]);
                        }
                    }
                    foreach (var kvp in settingsToInject) lines.Add($"{kvp.Key}:{kvp.Value}");
                    File.WriteAllLines(optionsFilePath, lines);
                }
                catch { }
            }
            else
            {
                try
                {
                    var newLines = settingsToInject.Select(kvp => $"{kvp.Key}:{kvp.Value}").ToList();
                    File.WriteAllLines(optionsFilePath, newLines);
                }
                catch { }
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

                string targetVersion = GetTargetVersionName();

                if (!_isInstalled)
                {
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

                // --- BƠM CẤU HÌNH ĐỒ HỌA TRƯỚC KHI VÀO GAME ---
                ApplyGraphicsPresetToMinecraft(optionsFile);

                var launchOption = new MLaunchOption
                {
                    Session = MSession.GetOfflineSession(_username),
                    MaximumRamMb = _appSettings.AllocatedRam, // Sử dụng RAM từ JSON
                    ServerIp = string.IsNullOrEmpty(_serverManifest.Server_Ip) ? "127.0.0.1" : _serverManifest.Server_Ip,
                    ServerPort = _serverManifest.Server_Port > 0 ? _serverManifest.Server_Port : 25565
                };

                if (isFullScreen) launchOption.FullScreen = true;
                else { launchOption.ScreenWidth = (int)SystemParameters.WorkArea.Width; launchOption.ScreenHeight = (int)SystemParameters.WorkArea.Height; }

                var process = await launcher.CreateProcessAsync(targetVersion, launchOption);

                Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_SyncServer"); });
                await SyncModsAsync(Path.Combine(path.BasePath, "mods"));

                Dispatcher.Invoke(() => CheckInstallationStatus());

                Dispatcher.Invoke(() => { if (this.FindName("txtDownloadStatus") is TextBlock t) t.Text = GetLang("msg_ConfigSkin"); });
                try
                {
                    string cslFolder = Path.Combine(path.BasePath, "CustomSkinLoader");
                    if (!Directory.Exists(cslFolder)) Directory.CreateDirectory(cslFolder);
                    File.WriteAllText(Path.Combine(cslFolder, "CustomSkinLoader.json"), @"
                    {
                      ""version"": ""14.12"",
                      ""loadlist"": [
                        { ""name"": ""OtonashiRei_LocalServer"", ""type"": ""Legacy"", ""skin"": """ + API_SERVER_URL + @"/skins/{USERNAME}.png"", ""checkPNG"": true },
                        { ""name"": ""Mojang"", ""type"": ""MojangAPI"" }
                      ],
                      ""enableDynamicSkull"": true, ""enableTransparentSkin"": true, ""ignoreHttpsCertificate"": false
                    }");
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

                Dispatcher.Invoke(() =>
                {
                    if (progressContainer != null) progressContainer.Visibility = Visibility.Collapsed;
                    _isInstalled = true;
                    if (this.FindName("txtVersionLabel") is TextBlock lblVersion) lblVersion.Text = GetLang("lbl_CurrentVersion");

                    btnPlay.Content = GetLang("btn_Playing");
                    btnPlay.IsEnabled = false;
                    NotificationManager.Show(GetLang("msg_InGame"), GetLang("msg_Connecting"));
                    this.WindowState = WindowState.Minimized;
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
                        if (this.FindName("txtDownloadStatus") is TextBlock txtStat) txtStat.Text = $"[Phục hồi] {fileEvent.FileName}";
                        if (this.FindName("txtDownloadDetail") is TextBlock txtDet) txtDet.Text = $"{fileEvent.ProgressedFileCount} / {fileEvent.TotalFileCount}";
                        if (pBar != null) { pBar.Maximum = fileEvent.TotalFileCount; pBar.Value = fileEvent.ProgressedFileCount; }
                        
                        double pct = fileEvent.TotalFileCount > 0 ? ((double)fileEvent.ProgressedFileCount / fileEvent.TotalFileCount) * 100 : 0;
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

            bool confirm = NotificationManager.ShowConfirm(title, desc);
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
            if (string.IsNullOrEmpty(serverVer) || string.IsNullOrEmpty(clientVer)) return false;

            serverVer = serverVer.TrimStart('v', 'V').Trim();
            clientVer = clientVer.TrimStart('v', 'V').Trim();

            if (serverVer == clientVer) return false;

            string[] sParts = serverVer.Split('-');
            string[] cParts = clientVer.Split('-');

            if (Version.TryParse(sParts[0], out Version sVersion) && Version.TryParse(cParts[0], out Version cVersion))
            {
                if (sVersion > cVersion) return true;
                if (sVersion < cVersion) return false;

                if (sVersion == cVersion)
                {
                    if (sParts.Length == 1 && cParts.Length > 1) return false;
                    if (sParts.Length > 1 && cParts.Length == 1) return true;

                    if (sParts.Length > 1 && cParts.Length > 1)
                    {
                        return string.Compare(sParts[1], cParts[1], StringComparison.OrdinalIgnoreCase) > 0;
                    }
                }
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSyncTimer?.Stop();
            Application.Current.Shutdown();
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _autoSyncTimer?.Stop();

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
        public List<string>? Mods { get; set; }
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
        public int AllocatedRam { get; set; } = 4096;
        public string GraphicsPreset { get; set; } = "Medium";
        public string Language { get; set; } = "EN";
        public string InstallPath { get; set; } = "";
    }
}
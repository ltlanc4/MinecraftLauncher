using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MinecraftLauncher.ViewModels
{
    public class UpdateViewModel : INotifyPropertyChanged
    {
        private string _downloadUrl;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // 🟢 BỘ TỪ ĐIỂN ĐA NGÔN NGỮ
        private Dictionary<string, string> _langDict = new Dictionary<string, string>();
        public string this[string key] => _langDict != null && _langDict.ContainsKey(key) ? _langDict[key] : key;

        #region Các thuộc tính Binding
        // 🟢 Các thuộc tính cố định cho XAML tự động lấy ngôn ngữ
        public string WindowTitle => this["msgUpdateWindowTitle"];
        public string HeaderText => this["msgUpdateHeader"].ToUpper();

        private double _downloadProgress = 0;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        private string _percentageText = "0%";
        public string PercentageText
        {
            get => _percentageText;
            set { _percentageText = value; OnPropertyChanged(); }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private bool _isIndeterminate = false;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set { _isIndeterminate = value; OnPropertyChanged(); }
        }
        #endregion

        public UpdateViewModel(string downloadUrl)
        {
            _downloadUrl = downloadUrl;
            LoadLanguage();
            
            // Khởi tạo chữ mặc định lúc vừa mở cửa sổ
            StatusText = this["msgUpdateConnecting"];
        }

        // 🟢 HÀM ĐỌC NGÔN NGỮ TỪ FILE
        private void LoadLanguage()
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher");
                string settingsFile = Path.Combine(appData, "launcher_settings.json");
                bool isEnglish = false;
                
                // Đọc file settings để xem user đang dùng tiếng Anh hay Việt
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    if (json.Contains("\"Language\": \"EN\"") || json.Contains("\"Language\":\"EN\""))
                        isEnglish = true;
                }

                // Nạp file .pak tương ứng
                string langFile = isEnglish ? "lang/en.pak" : "lang/vi.pak";
                if (File.Exists(langFile))
                {
                    string langJson = File.ReadAllText(langFile);
                    _langDict = JsonSerializer.Deserialize<Dictionary<string, string>>(langJson);
                }
            }
            catch { }
        }

        public async Task StartUpdateAsync()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string zipPath = Path.Combine(baseDir, "update.zip");
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe)) return;

            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        bool isMoreToRead = true;
                        long totalRead = 0;

                        while (isMoreToRead)
                        {
                            int read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0) isMoreToRead = false;
                            else
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                if (totalBytes.HasValue)
                                {
                                    double pct = (double)totalRead / totalBytes.Value * 100;
                                    DownloadProgress = pct;
                                    PercentageText = $"{pct:0}%";
                                    
                                    // 🟢 Truyền dung lượng vào chuỗi Format đa ngôn ngữ
                                    StatusText = string.Format(this["msgUpdateDownloading"], 
                                                 (totalRead / 1048576.0).ToString("0.##"), 
                                                 (totalBytes.Value / 1048576.0).ToString("0.##"));
                                }
                            }
                        }
                    }
                }

                // 🟢 Đổi Text cài đặt
                StatusText = this["msgUpdateInstalling"];
                IsIndeterminate = true;
                await Task.Delay(500); 

                string currentExeName = Path.GetFileName(currentExe);
                string batPath = Path.Combine(baseDir, "update.bat");

                string batContent = $@"
@echo off
timeout /t 3 /nobreak > NUL
powershell -Command ""Expand-Archive -Path 'update.zip' -DestinationPath '.' -Force""
del ""update.zip""
start """" ""{currentExeName}""
del ""%~f0""
";
                File.WriteAllText(batPath, batContent);

                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => 
                {
                    // 🟢 Báo lỗi đa ngôn ngữ
                    NotificationManager.Show(this["msgUpdateErrorTitle"], ex.Message);
                    Application.Current.Shutdown();
                });
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
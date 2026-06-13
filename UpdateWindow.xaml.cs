using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace MinecraftLauncher
{
    public partial class UpdateWindow : Window
    {
        private string _downloadUrl;
        private static readonly HttpClient _httpClient = new HttpClient();

        public UpdateWindow(string downloadUrl)
        {
            InitializeComponent();
            _downloadUrl = downloadUrl;
            this.Loaded += UpdateWindow_Loaded;
        }

        private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string zipPath = Path.Combine(baseDir, "update.zip");
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;

            try
            {
                // 1. TẢI FILE TỪNG BYTE VÀ CẬP NHẬT THANH TRƯỢT
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
                                    pbDownload.Value = pct;
                                    txtPercentage.Text = $"{pct:0}%";
                                    txtStatus.Text = $"Đang tải: {(totalRead / 1048576.0):0.##} MB / {(totalBytes.Value / 1048576.0):0.##} MB";
                                }
                            }
                        }
                    }
                }

                // 2. TẠO KỊCH BẢN GIẢI NÉN ĐỘC LẬP BÊN NGOÀI
                txtStatus.Text = "Đang cài đặt bản cập nhật...";
                pbDownload.IsIndeterminate = true;
                await Task.Delay(500); 

                string currentExeName = Path.GetFileName(currentExe);
                string batPath = Path.Combine(baseDir, "update.bat");

                // Script này sẽ chờ C# tắt hẳn (nhả khóa DLL) rồi mới giải nén
                string batContent = $@"
@echo off
:: Chờ 3 giây để Launcher nhả toàn bộ quyền khóa file DLL
timeout /t 3 /nobreak > NUL

:: Dùng PowerShell giải nén đè lên thư mục hiện tại
powershell -Command ""Expand-Archive -Path 'update.zip' -DestinationPath '.' -Force""

:: Xóa file zip rác
del ""update.zip""

:: Khởi động lại Launcher
start """" ""{currentExeName}""

:: Tự hủy file bat
del ""%~f0""
";
                File.WriteAllText(batPath, batContent);

                // 3. KÍCH HOẠT SCRIPT NGẦM VÀ TỰ SÁT ĐỂ NHẢ RAM
                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi cập nhật: " + ex.Message, "LỖI");
                Application.Current.Shutdown();
            }
        }
    }
}
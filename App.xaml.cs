using System.Configuration;
using System.Data;
using System.Windows;
using MinecraftLauncher;

namespace Minecraft_Launcher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex _mutex = null;
        private static EventWaitHandle _eventWaitHandle;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            
            // 1. Tạo "Ổ khóa" Mutex để Windows biết đây là bản Launcher duy nhất
            _mutex = new Mutex(true, "OtonashiRei_Launcher_Mutex", out createdNew);

            if (!createdNew)
            {
                // NẾU BẢN 1 ĐÃ CHẠY RỒI MÀ NGƯỜI CHƠI BẤM MỞ TIẾP BẢN 2:
                try
                {
                    // Kết nối với "Chuông báo thức" của Bản 1 và bấm chuông!
                    _eventWaitHandle = EventWaitHandle.OpenExisting("OtonashiRei_Launcher_Wakeup");
                    _eventWaitHandle.Set(); 
                }
                catch { }

                // Tự sát ngay lập tức cái bản thứ 2 này để không tạo thêm cửa sổ rác
                Application.Current.Shutdown();
                return;
            }

            // ==========================================================
            // NẾU ĐÂY LÀ BẢN ĐẦU TIÊN ĐƯỢC BẬT:
            // ==========================================================
            
            // 2. Thiết lập "Chuông báo thức" ngầm trên Windows
            _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, "OtonashiRei_Launcher_Wakeup");

            // 3. Tạo một tiểu trình (Thread) liên tục vểnh tai nghe chuông
            Thread wakeupThread = new Thread(() =>
            {
                while (_eventWaitHandle.WaitOne())
                {
                    Current.Dispatcher.Invoke(() =>
                    {
                        bool isHomeWindowFound = false;

                        // Tìm xem sảnh chính (HomeWindow) có đang chạy (hoặc ẩn dưới khay) không
                        foreach (Window win in Current.Windows)
                        {
                            if (win is HomeWindow homeWindow)
                            {
                                homeWindow.RestoreFromTray(); // Kéo nó từ dưới khay lên
                                isHomeWindowFound = true;
                                break;
                            }
                        }

                        // Nếu khách mới chỉ bật đến màn Đăng nhập (MainWindow)
                        if (!isHomeWindowFound && Current.MainWindow != null)
                        {
                            Current.MainWindow.Show();
                            Current.MainWindow.WindowState = WindowState.Normal;
                            Current.MainWindow.Activate();
                        }
                    });
                }
            });
            
            wakeupThread.IsBackground = true;
            wakeupThread.Start();

            // Vẫn tiếp tục quy trình nạp giao diện bình thường
            base.OnStartup(e);
        }
}


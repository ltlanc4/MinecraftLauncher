using System.Collections.Generic;
using System.Windows;

namespace MinecraftLauncher
{
    public static class NotificationManager
    {
        // Danh sách các thông báo đang hiển thị
        private static List<NotificationWindow> _openNotifications = new List<NotificationWindow>();

        // Khoảng cách từ viền CỬA SỔ LAUNCHER và khoảng cách giữa các thẻ
        private const double BORDER_OFFSET = 20;
        private const double SPACING = 10;

        public static void Show(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                
                // Tránh lỗi Crash nếu cửa sổ chính chưa kịp hiện lên
                if (mainWindow == null || !mainWindow.IsVisible) return;

                var notification = new NotificationWindow(title, message);
                
                // LIÊN KẾT THÔNG BÁO VỚI CỬA SỔ CHÍNH
                notification.Owner = mainWindow;
                
                // FIX LỖI ĐÈ ỨNG DỤNG KHÁC: Ép buộc tắt chế độ "Luôn nổi trên cùng"
                notification.Topmost = false;
                
                // ẨN ICON: Không hiển thị icon thông báo dưới thanh Taskbar của Windows
                notification.ShowInTaskbar = false;

                notification.Closed += (s, e) =>
                {
                    _openNotifications.Remove(notification);
                    ReorganizeNotifications(mainWindow);
                };

                _openNotifications.Add(notification);

                // FIX LỖI TEXT: Cần gọi Show() trước (lúc này Opacity = 0 nên vẫn tàng hình) 
                // để WPF kịp tính toán ActualHeight dựa trên lượng Text thực tế.
                notification.Show();
                PositionNotification(notification, mainWindow);
            });
        }

        private static void PositionNotification(NotificationWindow notification, Window mainWindow)
        {
            // Sử dụng ActualHeight thay vì Height cố định
            double right = mainWindow.Left + mainWindow.ActualWidth - notification.Width - BORDER_OFFSET;
            double top = mainWindow.Top + mainWindow.ActualHeight - notification.ActualHeight - BORDER_OFFSET;

            // Xếp chồng nếu có nhiều thông báo
            foreach (var openNote in _openNotifications)
            {
                if (openNote != notification)
                {
                    top -= (openNote.ActualHeight + SPACING);
                }
            }

            notification.Left = right;
            notification.Top = top;
        }

        private static void ReorganizeNotifications(Window mainWindow)
        {
            if (mainWindow == null) return;

            double top = mainWindow.Top + mainWindow.ActualHeight - SPACING - BORDER_OFFSET;

            var reversedList = new List<NotificationWindow>(_openNotifications);
            reversedList.Reverse();

            foreach (var note in reversedList)
            {
                top -= note.ActualHeight;
                note.Top = top; 
                top -= SPACING;
            }
        }
    }
}
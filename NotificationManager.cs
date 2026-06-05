using System.Collections.Generic;
using System.Linq;
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
                // 1. TÌM CỬA SỔ ĐANG ĐƯỢC HIỂN THỊ ĐỂ LÀM "CHA"
                // Ưu tiên cửa sổ đang được thao tác, nếu không có thì lấy cửa sổ hiển thị đầu tiên
                Window ownerWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
                
                if (ownerWindow == null)
                    ownerWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);

                if (ownerWindow == null)
                    ownerWindow = Application.Current.MainWindow;

                var notification = new NotificationWindow(title, message);

                // 2. NẾU CÓ CỬA SỔ ĐANG MỞ -> NEO THÔNG BÁO VÀO CỬA SỔ ĐÓ
                if (ownerWindow != null && ownerWindow.IsVisible)
                {
                    notification.Owner = ownerWindow;
                    notification.Topmost = false; // Tránh đè lên Chrome, Word...
                }
                else
                {
                    // 3. FALLBACK: Nếu Launcher bị ẩn ngầm, ép thông báo luôn nổi lên màn hình Windows
                    notification.Topmost = true;
                }

                notification.ShowInTaskbar = false;

                notification.Closed += (s, e) =>
                {
                    _openNotifications.Remove(notification);
                    ReorganizeNotifications(ownerWindow);
                };

                _openNotifications.Add(notification);

                // Hiện thông báo (Opacity = 0 để lấy Height thực tế trước khi căn tọa độ)
                notification.Show();
                PositionNotification(notification, ownerWindow);
            });
        }

        private static void PositionNotification(NotificationWindow notification, Window ownerWindow)
        {
            double right, top;

            // Nếu không có cửa sổ cha, vẽ tọa độ dựa trên màn hình Desktop Windows
            if (ownerWindow == null || !ownerWindow.IsVisible)
            {
                var desktopArea = SystemParameters.WorkArea;
                right = desktopArea.Right - notification.Width - BORDER_OFFSET;
                top = desktopArea.Bottom - notification.ActualHeight - BORDER_OFFSET;
            }
            else // Vẽ tọa độ dựa trên góc dưới cùng bên phải của Cửa sổ Launcher
            {
                right = ownerWindow.Left + ownerWindow.ActualWidth - notification.Width - BORDER_OFFSET;
                top = ownerWindow.Top + ownerWindow.ActualHeight - notification.ActualHeight - BORDER_OFFSET;
            }

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

        private static void ReorganizeNotifications(Window ownerWindow)
        {
            double top;

            if (ownerWindow == null || !ownerWindow.IsVisible)
            {
                top = SystemParameters.WorkArea.Bottom - SPACING - BORDER_OFFSET;
            }
            else
            {
                top = ownerWindow.Top + ownerWindow.ActualHeight - SPACING - BORDER_OFFSET;
            }

            var reversedList = new List<NotificationWindow>(_openNotifications);
            reversedList.Reverse();

            foreach (var note in reversedList)
            {
                top -= note.ActualHeight;
                note.Top = top; 
                top -= SPACING;
            }
        }

        // Hàm mới: Trả về True nếu chọn Đồng Ý, False nếu chọn Hủy Bỏ
        public static bool ShowConfirm(string title, string message)
        {
            bool isConfirm = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                ConfirmWindow confirmWin = new ConfirmWindow(title, message);
                
                // Neo cửa sổ Confirm vào giữa cửa sổ chính đang mở
                if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                {
                    confirmWin.Owner = Application.Current.MainWindow;
                }
                
                // Mở dạng Dialog (Buộc người dùng phải tương tác mới cho dùng tiếp app)
                confirmWin.ShowDialog();
                
                isConfirm = confirmWin.Result;
            });
            return isConfirm;
        }
    }
}
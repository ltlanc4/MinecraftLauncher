using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MinecraftLauncher.ViewModels;

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
                Window ownerWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                  ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                                  ?? Application.Current.MainWindow;

                // 🟢 KHỞI TẠO THEO CHUẨN MVVM
                var viewModel = new NotificationViewModel(title, message);
                var notification = new NotificationWindow(viewModel);

                if (ownerWindow != null && ownerWindow.IsVisible)
                {
                    notification.Owner = ownerWindow;
                    notification.Topmost = false;
                }
                else
                {
                    notification.Topmost = true;
                }

                notification.ShowInTaskbar = false;
                notification.Closed += (s, e) =>
                {
                    _openNotifications.Remove(notification);
                    ReorganizeNotifications(ownerWindow);
                };

                _openNotifications.Add(notification);
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

        public static bool ShowConfirm(string title, string message, string confirmText = "ĐỒNG Ý", string cancelText = "HỦY BỎ")
        {
            bool result = false;
            if (Application.Current == null) return false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                // 🟢 KHỞI TẠO THEO CHUẨN MVVM
                var viewModel = new ConfirmViewModel(title, message, confirmText, cancelText);
                var dialog = new ConfirmWindow(viewModel);
                
                Window ownerWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                  ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                                  ?? Application.Current.MainWindow;

                if (ownerWindow != null && ownerWindow.IsVisible)
                {
                    dialog.Owner = ownerWindow;
                }

                result = dialog.ShowDialog() == true;
            });

            return result;
        }
    }
}
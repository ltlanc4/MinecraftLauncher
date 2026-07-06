using System;
using System.Windows;
using System.Windows.Input;

namespace MinecraftLauncher
{
    public partial class ConfirmWindow : Window
    {
        // Hàm khởi tạo nhận chuỗi văn bản đã dịch thuật từ file ngôn ngữ .pak
        public ConfirmWindow(string title, string message, string confirmText = "ĐỒNG Ý", string cancelText = "HỦY BỎ")
        {
            InitializeComponent();
            
            txtTitle.Text = title.ToUpper();
            txtMessage.Text = message;
            btnConfirm.Content = confirmText.ToUpper();
            btnCancel.Content = cancelText.ToUpper();
        }

        // Kéo giữ vị trí bất kỳ trên cửa sổ để di chuyển hộp thoại
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        // Bấm chọn Đồng ý -> Trả về kết quả True
        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        // Bấm chọn Hủy bỏ -> Trả về kết quả False
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        /// <summary>
        /// Hàm tĩnh tiện ích tối cao để thay thế MessageBox chuẩn MVVM.
        /// Tự động dò tìm cửa sổ đang mở để làm Owner và căn ra chính giữa sảnh chính.
        /// </summary>
        public static bool ShowModal(string title, string message, string confirmText = "ĐỒNG Ý", string cancelText = "HỦY BỎ")
        {
            bool result = false;
            
            if (Application.Current == null) return false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new ConfirmWindow(title, message, confirmText, cancelText);
                
                // Tự động neo hộp thoại vào cửa sổ cha đang hiển thị để tránh hiện tượng khuất sau màn hình
                Window activeWindow = Application.Current.MainWindow;
                if (activeWindow != null && activeWindow.IsVisible)
                {
                    dialog.Owner = activeWindow;
                }

                result = dialog.ShowDialog() == true;
            });

            return result;
        }
    }
}
using System;
using System.Windows.Input;

namespace MinecraftLauncher.ViewModels
{
    public class ConfirmViewModel
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string ConfirmText { get; set; }
        public string CancelText { get; set; }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        // Sự kiện báo cáo lại cho View biết cần phải đóng cửa sổ và trả kết quả gì
        public event Action<bool> RequestClose;

        public ConfirmViewModel(string title, string message, string confirmText, string cancelText)
        {
            Title = title.ToUpper();
            Message = message;
            ConfirmText = confirmText.ToUpper();
            CancelText = cancelText.ToUpper();

            ConfirmCommand = new RelayCommand((p) => RequestClose?.Invoke(true));
            CancelCommand = new RelayCommand((p) => RequestClose?.Invoke(false));
        }
    }
}
using System;
using System.Windows.Input;
using System.Windows.Threading;

namespace MinecraftLauncher.ViewModels
{
    public class NotificationViewModel
    {
        public string Title { get; set; }
        public string Message { get; set; }

        public ICommand CloseCommand { get; }
        public event Action RequestClose;

        private DispatcherTimer _closeTimer;

        public NotificationViewModel(string title, string message)
        {
            Title = title.ToUpper();
            Message = message;

            CloseCommand = new RelayCommand((p) => ExecuteClose());

            // Logic Timer tự đóng đưa vào ViewModel
            _closeTimer = new DispatcherTimer();
            _closeTimer.Interval = TimeSpan.FromSeconds(4);
            _closeTimer.Tick += (s, e) => ExecuteClose();
            _closeTimer.Start();
        }

        private void ExecuteClose()
        {
            if (_closeTimer != null)
            {
                _closeTimer.Stop();
                _closeTimer = null;
            }
            RequestClose?.Invoke(); // Ra lệnh cho View chạy Animation đóng
        }
    }
}
using System.Windows;
using System.Windows.Input;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher
{
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow(ConfirmViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel; // Nạp bộ não MVVM

            // Lắng nghe lệnh đóng từ ViewModel
            viewModel.RequestClose += (result) => 
            {
                this.DialogResult = result;
                this.Close();
            };
        }

        // Kéo giữ khung viền (Thuần túy là thao tác UI nên được phép ở lại Code-Behind)
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }
    }
}
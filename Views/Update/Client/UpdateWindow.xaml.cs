using System.Windows;
using MinecraftLauncher.ViewModels; // Thêm dòng này để gọi ViewModel

namespace MinecraftLauncher
{
    public partial class UpdateWindow : Window
    {
        public UpdateWindow(string downloadUrl)
        {
            InitializeComponent();
            
            // 🟢 Nạp bộ não MVVM cho giao diện
            var viewModel = new UpdateViewModel(downloadUrl);
            this.DataContext = viewModel;

            // 🟢 Yêu cầu Bộ não bắt đầu làm việc ngay khi Cửa sổ load xong
            this.Loaded += async (s, e) => await viewModel.StartUpdateAsync();
        }
    }
}
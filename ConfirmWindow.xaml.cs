using System.IO;
using System.Windows;

namespace MinecraftLauncher
{
    public partial class ConfirmWindow : Window
    {
        public bool Result { get; private set; } = false;

        public ConfirmWindow(string title, string message)
        {
            InitializeComponent();
            txtTitle.Text = title;
            txtMessage.Text = message;

            // ========================================================
            // TỰ ĐỘNG ĐỌC NGÔN NGỮ ĐỂ ĐỔI CHỮ CHO 2 NÚT BẤM
            // ========================================================
            string langFile = "lang.txt";
            bool isEnglish = false;
            
            if (File.Exists(langFile))
            {
                isEnglish = File.ReadAllText(langFile).Trim() == "EN";
            }

            if (isEnglish)
            {
                btnYes.Content = "YES";
                btnNo.Content = "CANCEL";
            }
            else
            {
                btnYes.Content = "ĐỒNG Ý";
                btnNo.Content = "HỦY BỎ";
            }
        }

        private void btnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = true; // Xác nhận
            this.Close();
        }

        private void btnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = false; // Hủy bỏ
            this.Close();
        }
    }
}
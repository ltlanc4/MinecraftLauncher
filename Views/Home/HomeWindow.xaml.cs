using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Text.Json;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher
{
    public partial class HomeWindow : Window
    {
        private readonly HomeViewModel _viewModel;
        private System.Windows.Forms.NotifyIcon _notifyIcon;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public HomeWindow(string username, string token, string uuid)
        {
            InitializeComponent();

            _viewModel = new HomeViewModel(username);
            this.DataContext = _viewModel;

            // Đăng ký nhận sự kiện từ bộ não ViewModel
            _viewModel.OnShowConfirmDialog += ViewModel_OnShowConfirmDialog;
            _viewModel.OnRequestCloseOrHide += ViewModel_OnRequestCloseOrHide;
            _viewModel.OnLogout += ViewModel_OnLogout;

            InitializeNotifyIcon();
        }

        private void ViewModel_OnShowConfirmDialog(string titleKey, string descKey, string confirmKey, string cancelKey)
        {
            if (NotificationManager.ShowConfirm(_viewModel[titleKey], _viewModel[descKey], _viewModel[confirmKey], _viewModel[cancelKey]))
            {
                string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher", "launcher_settings.json");
                
                Dictionary<string, object> settings = new Dictionary<string, object>();
                bool currentIsEng = true;
                
                if (File.Exists(settingsFile)) 
                {
                    try { 
                        // Đã sửa lại thành File.ReadAllText đồng bộ
                        string json = File.ReadAllText(settingsFile);
                        settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? settings; 
                        if (settings.TryGetValue("Language", out object lang)) currentIsEng = lang.ToString() == "EN";
                    } catch { }
                }
                
                settings["Language"] = currentIsEng ? "VI" : "EN";
                File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

                System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                Application.Current.Shutdown();
            }
        }

        private void ViewModel_OnRequestCloseOrHide()
        {
            if (_viewModel.CloseMode == 2) HideToTray();
            else _viewModel.CloseOrExitRequested();
        }

        private void ViewModel_OnLogout()
        {
            if (_notifyIcon != null) _notifyIcon.Dispose();
            
            this.IsHitTestVisible = false;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fadeOut.Completed += (s, e) => {
                MainWindow login = new MainWindow();
                login.Opacity = 0;
                login.Show();
                login.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
                this.Close();
            };
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon { Text = "OtonashiRei MC Server", Visible = false };
            try { _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName); } catch { _notifyIcon.Icon = System.Drawing.SystemIcons.Application; }

            _notifyIcon.MouseUp += (s, e) => {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) RestoreFromTray();
                else if (e.Button == System.Windows.Forms.MouseButtons.Right) {
                    ContextMenu trayMenu = (ContextMenu)this.FindResource("TrayContextMenu");
                    trayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    trayMenu.IsOpen = true;
                    var hwndSource = PresentationSource.FromVisual(trayMenu) as System.Windows.Interop.HwndSource;
                    if (hwndSource != null) SetForegroundWindow(hwndSource.Handle);
                }
            };
        }

        private void HideToTray() { _notifyIcon.Visible = true; this.Hide(); NotificationManager.Show(_viewModel["msgTrayNotifyTitle"], _viewModel["msgTrayNotifyDesc"]); }
        public void RestoreFromTray() { this.Show(); this.WindowState = WindowState.Normal; this.Activate(); _notifyIcon.Visible = false; }

        private void PlayTransitionAnimation(FrameworkElement target, params FrameworkElement[] hides)
        {
            if (target == null) return;
            // Đã sửa chữ "panel" thành "p"
            foreach (var p in hides) if (p != null) p.Visibility = Visibility.Collapsed; 
            
            target.Visibility = Visibility.Visible; target.Opacity = 0;
            TranslateTransform trans = new TranslateTransform(0, 15); target.RenderTransform = trans;
            target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            trans.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(15, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => this.DragMove();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => ViewModel_OnRequestCloseOrHide();

        // Xử lý Animation thuần đồ hoạ chuyển Tab
        private void TabHome_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(ViewHome, ViewProfile, ViewSettings);
        private void TabProfile_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(ViewProfile, ViewHome, ViewSettings);
        private void TabSettings_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(ViewSettings, ViewHome, ViewProfile);
        
        private void SubTabInfo_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(pnlProfileInfo, pnlProfileEmail, pnlProfilePassword, pnlProfileSkin);
        private void SubTabSkin_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(pnlProfileSkin, pnlProfileInfo, pnlProfileEmail, pnlProfilePassword);
        private void SubTabEmail_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(pnlProfileEmail, pnlProfileInfo, pnlProfilePassword, pnlProfileSkin);
        private void SubTabPassword_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(pnlProfilePassword, pnlProfileInfo, pnlProfileEmail, pnlProfileSkin);
        private void SubSetGeneral_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(pnlSettingsGeneral, pnlSettingsGraphics);
        private void SubSetGraphics_Checked(object sender, RoutedEventArgs e) => PlayTransitionAnimation(pnlSettingsGraphics, pnlSettingsGeneral);

        private void TrayMenuRestore_Click(object sender, RoutedEventArgs e) => RestoreFromTray();
        private void TrayMenuExit_Click(object sender, RoutedEventArgs e) => _viewModel.CloseOrExitRequested();

        private void btnGameMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void CloseOption_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rad && int.TryParse(rad.Tag?.ToString(), out int mode))
            {
                if (this.DataContext is HomeViewModel vm) vm.CloseMode = mode;
            }
        }
    }
}
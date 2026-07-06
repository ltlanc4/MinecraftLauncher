using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Reflection;
using DotNetEnv;

namespace MinecraftLauncher.ViewModels
{
    public enum ActivePanelState
    {
        Login,
        Register,
        ForgotPasswordStep1,
        ForgotPasswordStep2
    }

    public class LoginViewModel : ViewModelBase
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _appDataFolder;
        private readonly string _sessionFile;
        private readonly string _settingsFile;
        private readonly string _envPath;
        private string _apiUrl;

        private ActivePanelState _currentPanel = ActivePanelState.Login;
        private bool _isLoading = false;
        private bool _rememberMe = false;
        private bool _isEnglish = true;
        private Dictionary<string, string> _langDict = new Dictionary<string, string>();

        private string _loginUsername;
        private string _regUsername;
        private string _regEmail;
        private string _recoveryUsername;
        private string _recoveryEmail;
        private string _recoveryCode;

        public event Action<ActivePanelState, bool> OnPanelChanged;
        public event Action OnLoginSuccess;

        #region Các thuộc tính Binding dữ liệu
        
        public LoginViewModel Lang => this;

        public ActivePanelState CurrentPanel
        {
            get => _currentPanel;
            set => SetProperty(ref _currentPanel, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool RememberMe
        {
            get => _rememberMe;
            set => SetProperty(ref _rememberMe, value);
        }

        public bool IsEnglish
        {
            get => _isEnglish;
            set
            {
                if (SetProperty(ref _isEnglish, value))
                {
                    LoadLanguagePack();
                }
            }
        }

        // Bộ từ điển lấy ngôn ngữ động
        public string this[string key]
        {
            get
            {
                if (_langDict != null && _langDict.TryGetValue(key, out string value))
                {
                    return value.Replace("\\n", Environment.NewLine);
                }
                return key;
            }
        }

        public string LangBtnText => IsEnglish ? "EN-US" : "VI-VN";

        public string LoginUsername { get => _loginUsername; set => SetProperty(ref _loginUsername, value); }
        public string RegUsername { get => _regUsername; set => SetProperty(ref _regUsername, value); }
        public string RegEmail { get => _regEmail; set => SetProperty(ref _regEmail, value); }
        public string RecoveryUsername { get => _recoveryUsername; set => SetProperty(ref _recoveryUsername, value); }
        public string RecoveryEmail { get => _recoveryEmail; set => SetProperty(ref _recoveryEmail, value); }
        public string RecoveryCode { get => _recoveryCode; set => SetProperty(ref _recoveryCode, value); }

        private string _loadingMessageKey = "lblLoadingLogin";
        public string LoadingMessage => this[_loadingMessageKey];

        #endregion

        #region Commands
        public ICommand LoginCommand { get; }
        public ICommand RegisterCommand { get; }
        public ICommand SendRecoveryCodeCommand { get; }
        public ICommand ConfirmResetPasswordCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand SwitchPanelCommand { get; }
        #endregion

        public LoginViewModel()
        {
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftLauncher");
            if (!Directory.Exists(_appDataFolder)) Directory.CreateDirectory(_appDataFolder);

            _sessionFile = Path.Combine(_appDataFolder, "session_data.json");
            _settingsFile = Path.Combine(_appDataFolder, "launcher_settings.json");

            InitializeEnvironment();
            LoadSettings();

            LoginCommand = new RelayCommand(async (p) => await ExecuteLogin(p), (p) => !IsLoading);
            RegisterCommand = new RelayCommand(async (p) => await ExecuteRegister(p), (p) => !IsLoading);
            SendRecoveryCodeCommand = new RelayCommand(async (p) => await ExecuteSendRecoveryCode(p), (p) => !IsLoading);
            ConfirmResetPasswordCommand = new RelayCommand(async (p) => await ExecuteConfirmResetPassword(p), (p) => !IsLoading);
            ChangeLanguageCommand = new RelayCommand((p) => ExecuteChangeLanguage());
            SwitchPanelCommand = new RelayCommand((p) => ExecuteSwitchPanel(p));

            LoadSavedSession();
        }

        private void InitializeEnvironment()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("MinecraftLauncher.default.env"))
            {
                if (stream != null)
                {
                    Env.Load(stream);
                }
            }
            _apiUrl = $"http://{Env.GetString("SERVER_API_IP")}:{Env.GetString("SERVER_API_PORT")}/auth";
        }

        private void LoadSettings()
        {
            if (File.Exists(_settingsFile))
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_settingsFile));
                    if (settings != null && settings.TryGetValue("Language", out JsonElement langElement))
                    {
                        _isEnglish = langElement.GetString() == "EN";
                    }
                }
                catch { _isEnglish = true; }
            }
            LoadLanguagePack();
        }

        private void LoadLanguagePack()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string langFile = Path.Combine(baseDir, _isEnglish ? "lang/en.pak" : "lang/vi.pak");
            try
            {
                if (File.Exists(langFile))
                {
                    string jsonContent = File.ReadAllText(langFile, Encoding.UTF8);
                    _langDict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch { }

            OnPropertyChanged(nameof(Lang));
            OnPropertyChanged(nameof(LangBtnText));
            OnPropertyChanged("Item[]"); 
        }

        private void LoadSavedSession()
        {
            if (File.Exists(_sessionFile))
            {
                try
                {
                    var session = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_sessionFile));
                    if (session != null && session.TryGetValue("Username", out string user) && session.TryGetValue("Password", out string pass))
                    {
                        LoginUsername = user;
                        _rememberMe = true;
                        OnPropertyChanged(nameof(RememberMe));
                        
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                            if (Application.Current.MainWindow is MainWindow view)
                            {
                                string decodedPass = Encoding.UTF8.GetString(Convert.FromBase64String(pass));
                                view.txtLoginPassword.Password = decodedPass;
                                ExecuteLogin(view.txtLoginPassword);
                            }
                        }));
                    }
                }
                catch { try { File.Delete(_sessionFile); } catch { } }
            }
        }

        private void ExecuteChangeLanguage()
        {
            string message = this["msgRestartRequiredDesc"];
            string title = this["msgRestartRequiredTitle"];
            string confirmTxt = this["btnConfirm"];
            string cancelTxt = this["btnCancel"];

            if (NotificationManager.ShowConfirm(title, message, confirmTxt, cancelTxt))
            {
                IsEnglish = !IsEnglish;
                
                Dictionary<string, object> currentSettings = new Dictionary<string, object>();
                if (File.Exists(_settingsFile))
                {
                    try { currentSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(_settingsFile)) ?? new Dictionary<string, object>(); } catch { }
                }
                currentSettings["Language"] = IsEnglish ? "EN" : "VI";
                File.WriteAllText(_settingsFile, JsonSerializer.Serialize(currentSettings, new JsonSerializerOptions { WriteIndented = true }));

                System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                Application.Current.Shutdown();
            }
        }

        private void ExecuteSwitchPanel(object param)
        {
            if (param == null) return;
            string stateStr = param.ToString();
            ActivePanelState targetState = (ActivePanelState)Enum.Parse(typeof(ActivePanelState), stateStr);
            
            // TỰ ĐỘNG XÓA TRẮNG DỮ LIỆU KHI QUAY VỀ MÀN HÌNH LOGIN
            if (targetState == ActivePanelState.Login)
            {
                RegUsername = string.Empty;
                RegEmail = string.Empty;
                RecoveryUsername = string.Empty;
                RecoveryEmail = string.Empty;
                RecoveryCode = string.Empty;
            }

            bool slideRight = targetState == ActivePanelState.Login;
            OnPanelChanged?.Invoke(targetState, slideRight);
            CurrentPanel = targetState;
        }

        private void SetLoadingState(bool isLoading, string messageKey = "lblLoadingLogin")
        {
            _loadingMessageKey = messageKey;
            OnPropertyChanged(nameof(LoadingMessage));
            IsLoading = isLoading;
        }

        private async Task ExecuteLogin(object passwordContainer)
        {
            var passwordBox = passwordContainer as System.Windows.Controls.PasswordBox;
            string password = passwordBox?.Password ?? "";

            if (string.IsNullOrWhiteSpace(LoginUsername) || string.IsNullOrWhiteSpace(password))
            {
                NotificationManager.Show(this["msgWarning"], this["msgLoginEmpty"]);
                return;
            }

            try
            {
                SetLoadingState(true, "lblLoadingLogin");
                await Task.Delay(1500);

                var content = new StringContent(JsonSerializer.Serialize(new { username = LoginUsername, password = password }), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"{_apiUrl}/login", content);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!responseString.Trim().StartsWith("{")) throw new Exception(this["msgServerHtml"]);

                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseString);

                if (response.IsSuccessStatusCode && result != null && result.TryGetValue("success", out JsonElement successObj) && successObj.GetBoolean())
                {
                    if (RememberMe)
                    {
                        var sessionData = new { Username = LoginUsername, Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password)) };
                        File.WriteAllText(_sessionFile, JsonSerializer.Serialize(sessionData));
                    }
                    else if (File.Exists(_sessionFile)) File.Delete(_sessionFile);

                    OnLoginSuccess?.Invoke();
                }
                else
                {
                    IsLoading = false;
                    string msg = result != null && result.TryGetValue("message", out JsonElement m) ? m.GetString() : this["msgLoginFail"];
                    NotificationManager.Show(this["msgError"], msg);
                }
            }
            catch (Exception ex)
            {
                IsLoading = false;
                NotificationManager.Show(this["msgConnectionError"], ex.Message);
            }
        }

        private async Task ExecuteRegister(object passContainer)
        {
            var array = passContainer as object[];
            var pBox = array?[0] as System.Windows.Controls.PasswordBox;
            var cPBox = array?[1] as System.Windows.Controls.PasswordBox;

            string pass = pBox?.Password ?? "";
            string confirm = cPBox?.Password ?? "";

            if (string.IsNullOrWhiteSpace(RegUsername) || string.IsNullOrWhiteSpace(RegEmail) || string.IsNullOrWhiteSpace(pass))
            {
                NotificationManager.Show(this["msgWarning"], this["msgRegEmpty"]);
                return;
            }
            if (pass != confirm)
            {
                NotificationManager.Show(this["msgError"], this["msgPassNotMatch"]);
                return;
            }

            try
            {
                SetLoadingState(true, "lblLoadingRegistering");
                await Task.Delay(1500);

                var content = new StringContent(JsonSerializer.Serialize(new { username = RegUsername, email = RegEmail, password = pass }), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"{_apiUrl}/register", content);
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.Content.ReadAsStringAsync());

                if (response.IsSuccessStatusCode && result != null && result.TryGetValue("success", out JsonElement s) && s.GetBoolean())
                {
                    NotificationManager.Show(this["msgSuccess"], this["msgRegSuccess"]);
                    ExecuteSwitchPanel("Login");
                }
                else
                {
                    string msg = result != null && result.TryGetValue("message", out JsonElement m) ? m.GetString() : this["msgRegFail"];
                    NotificationManager.Show(this["msgError"], msg);
                }
            }
            catch (Exception ex) { NotificationManager.Show(this["msgConnectionError"], ex.Message); }
            finally { IsLoading = false; }
        }

        private async Task ExecuteSendRecoveryCode(object param)
        {
            if (string.IsNullOrWhiteSpace(RecoveryUsername) || string.IsNullOrWhiteSpace(RecoveryEmail))
            {
                NotificationManager.Show(this["msgWarning"], this["msgForgotEmpty"]);
                return;
            }

            try
            {
                SetLoadingState(true, "lblLoadingSending");
                await Task.Delay(1500);

                var content = new StringContent(JsonSerializer.Serialize(new { username = RecoveryUsername, email = RecoveryEmail }), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"{_apiUrl}/forgot-password", content);
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.Content.ReadAsStringAsync());

                if (response.IsSuccessStatusCode && result != null && result.TryGetValue("success", out JsonElement s) && s.GetBoolean())
                {
                    NotificationManager.Show(this["msgRecoverySentTitle"], this["msgForgotSuccess"]);
                    ExecuteSwitchPanel("ForgotPasswordStep2");
                }
                else
                {
                    string msg = result != null && result.TryGetValue("message", out JsonElement m) ? m.GetString() : this["msgForgotFail"];
                    NotificationManager.Show(this["msgError"], msg);
                }
            }
            catch (Exception ex) { NotificationManager.Show(this["msgConnectionError"], ex.Message); }
            finally { IsLoading = false; }
        }

        private async Task ExecuteConfirmResetPassword(object passContainer)
        {
            var array = passContainer as object[];
            var pBox = array?[0] as System.Windows.Controls.PasswordBox;
            var cPBox = array?[1] as System.Windows.Controls.PasswordBox;

            string newPass = pBox?.Password ?? "";
            string confirmPass = cPBox?.Password ?? "";

            if (string.IsNullOrWhiteSpace(RecoveryCode) || string.IsNullOrWhiteSpace(newPass))
            {
                NotificationManager.Show(this["msgWarning"], this["msgResetEmpty"]);
                return;
            }
            if (newPass != confirmPass)
            {
                NotificationManager.Show(this["msgError"], this["msgPassNotMatch"]);
                return;
            }

            try
            {
                SetLoadingState(true, "lblLoadingResetting");
                await Task.Delay(1500);

                var content = new StringContent(JsonSerializer.Serialize(new { username = RecoveryUsername, otp = RecoveryCode, newPassword = newPass }), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"{_apiUrl}/reset-password", content);
                var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.Content.ReadAsStringAsync());

                if (response.IsSuccessStatusCode && result != null && result.TryGetValue("success", out JsonElement s) && s.GetBoolean())
                {
                    NotificationManager.Show(this["msgSuccess"], this["msgResetSuccess"]);
                    ExecuteSwitchPanel("Login");
                }
                else
                {
                    string msg = result != null && result.TryGetValue("message", out JsonElement m) ? m.GetString() : this["msgResetFail"];
                    NotificationManager.Show(this["msgError"], msg);
                }
            }
            catch (Exception ex) { NotificationManager.Show(this["msgConnectionError"], ex.Message); }
            finally { IsLoading = false; }
        }
    }
}
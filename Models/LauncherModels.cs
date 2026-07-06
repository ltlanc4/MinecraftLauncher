using System.Collections.Generic;

namespace MinecraftLauncher // Khai báo chung namespace với dự án của bạn
{
    public class ModFileInfo
    {
        public string Name { get; set; }
        public string Hash { get; set; }
    }

    public class ServerInfoResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Version { get; set; }
        public string? Loader { get; set; }
        public string? Loader_Version { get; set; }
        public string? Server_Ip { get; set; }
        public int Server_Port { get; set; }
        public int TotalMods { get; set; }
        public List<ModFileInfo>? Mods { get; set; } 
    }

    public class ModStatusItem
    {
        public string FileName { get; set; }
        public bool IsInstalled { get; set; }
        public string StatusIcon => IsInstalled ? "✔" : "❌";
        public string StatusColor => IsInstalled ? "#4ADE80" : "#FF4D4D";
    }

    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
    }

    public class LauncherSettings
    {
        public int AllocatedRam { get; set; } = 8192;
        public string Language { get; set; } = "EN";
        public string InstallPath { get; set; } = "";
        public int CloseMode { get; set; } = 2; 
    }

    public class ApiResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public string? Username { get; set; }
        public string? Uuid { get; set; }
    }
}
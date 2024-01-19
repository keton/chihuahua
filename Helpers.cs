using chiuaua;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

internal static class Helpers {

    private static readonly string uevrDownloadURL = "https://github.com/praydog/UEVR/releases/latest/download/UEVR.zip";

    private static readonly string[] uevr_dlls = [
        "UEVRPluginNullifier.dll",
        "openxr_loader.dll",
        "UEVRBackend.dll",
    ];

    private static readonly string[] unwanted_game_plugins = [
        "OpenVR",
        "OpenXR",
        "Oculus"
    ];

    public static bool CheckDLLsPresent(string exePath) {
        foreach (string dll in uevr_dlls) {
            var dllWithPath = Path.Combine(exePath, dll);

            if (!Path.Exists(dllWithPath)) {
                return false;
            }
        }

        return true;
    }

    public static async Task DownloadUEVRAsync() {
        try {
            using var client = new HttpClient();
            using var s = await client.GetStreamAsync(uevrDownloadURL);
            using var stream = new MemoryStream();
            await s.CopyToAsync(stream);
            using var zipArchive = new ZipArchive(stream);

            foreach (var entry in zipArchive.Entries) {
                if (uevr_dlls.Contains(entry.Name)) {
                    entry.ExtractToFile(Path.Join(Path.GetDirectoryName(Environment.ProcessPath), entry.Name), true);
                }
            }

        } catch (Exception e) {
            ExitWithMessage("Error downloading: " + e.Message, 1);
        }
    }

    [DoesNotReturn]
    public static void ExitWithMessage(string message, int exitCode = 0) {
        Console.WriteLine(message);

        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();

        Environment.Exit(exitCode);
    }

    private static string? GetGamePluginsDir(string gameExe) {

        string? currentPath = Path.GetDirectoryName(gameExe);
        if (currentPath == null) {
            return null;
        }

        for (int i = 0; i < 5; i++) {
            if (Directory.Exists(Path.Join(currentPath, "Engine\\Binaries\\ThirdParty"))) {
                return Path.Join(currentPath, "Engine\\Binaries\\ThirdParty");
            }

            var parentDir = Directory.GetParent(currentPath);
            if (parentDir is not null) {
                currentPath = parentDir.FullName;
            } else {
                return null;
            }
        }

        return null;
    }

    // whole process group
    public static Process[] GetGameProceses(string gameExe) {
        var processName = Path.GetFileNameWithoutExtension(gameExe);

        return Process.GetProcessesByName(processName);
    }

    // main game process
    public static Process? GetMainGameProces(string gameExe) {

        var processName = Path.GetFileNameWithoutExtension(gameExe);

        foreach (var process in Process.GetProcessesByName(processName)) {

            if (process.MainWindowTitle.Length == 0) {
                continue;
            }

            foreach (ProcessModule module in process.Modules) {
                if (module.ModuleName.ToLower() == "d3d11.dll" || module.ModuleName.ToLower() == "d3d12.dll") {
                    return process;
                }
            }
        }

        return null;
    }

    public static void InjectDll(int processId, string dllName) {
        if (!Injector.InjectDll(processId, dllName)) {
            ExitWithMessage("Failed to inject: " + dllName, 1);
        }
    }

    public static bool IsProcessRunning(int processId) {
        try {
            Process.GetProcessById(processId);
        } catch (ArgumentException) {
            return false;
        }

        return true;
    }

    public static void NullifyPlugins(int processId) {
        IntPtr nullifierBase;
        if (Injector.InjectDll(processId, "UEVRPluginNullifier.dll", out nullifierBase) && nullifierBase.ToInt64() > 0) {
            if (!Injector.CallFunctionNoArgs(processId, "UEVRPluginNullifier.dll", nullifierBase, "nullify", true)) {
                Console.WriteLine("Failed to nullify VR plugins.");
            }
        } else {
            ExitWithMessage("Failed to inject: UEVRPluginNullifier.dll", 1);
        }
    }

    public static void RemoveUnwantedPlugins(string gameExe) {
        var gamePluginsDir = GetGamePluginsDir(gameExe);
        if (gamePluginsDir == null) { return; }

        foreach (var plugin in unwanted_game_plugins) {
            var pluginDir = Path.Join(gamePluginsDir, plugin);
            if (Directory.Exists(pluginDir)) {
                try {
                    Directory.Delete(pluginDir, true);
                    Console.WriteLine("Removed plugin: " + pluginDir);
                } catch (Exception e) {
                    Console.WriteLine("Failed to remove plugin " + pluginDir + "\nException: " + e.Message);
                }
            }
        }
    }

    public static void TryCloseGame(string gameExe) {
        foreach (var gameProcess in GetGameProceses(gameExe)) {
            try {
                gameProcess.Kill();
            } catch (Exception) {
            }
        }
    }

    public static async Task<bool> WaitForGameProcessAsync(string gameExe, int timeoutMs) {
        int elapsed = 0;

        Process? gameProcess = null;

        while ((elapsed < timeoutMs) && (gameProcess = GetMainGameProces(gameExe)) == null) {
            elapsed += 100;
            await Task.Delay(100);
        }

        return gameProcess != null;
    }
}
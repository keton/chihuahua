using GitHub;
using GitHub.Client;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Spectre.Console;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Handlers;

namespace chihuahua {

    internal enum RuntimeType {
        OpenVR,
        OpenXR,
        Auto
    }

    internal enum UEVRBuild {
        Release,
        Nightly
    }

    internal struct LaunchOptions {
        public string gameExe;
        public string? launchCmd;
        public string? launchCmdArgs;
        public int injectionDelayS;
        public RuntimeType runtime;
        public UEVRBuild uevrBuild;
    };

    internal static class Helpers {

        private static readonly string[] uevr_dlls = [
            "UEVRPluginNullifier.dll",
            "openxr_loader.dll",
            "openvr_api.dll",
            "UEVRBackend.dll",
        ];

        private static readonly string[] unwanted_game_plugins = [
            "OpenVR",
            "OpenXR",
            "Oculus"
        ];

        private enum VRSoftware {
            VirtualDesktop,
            Oculus,
            Pico,
            SteamVR,
            Unknown
        }

        private enum OpenXRRuntime {
            VirtualDesktop,
            Oculus,
            Steam,
            Varjo,
            ViveVR,
            WMR,
            Unknown
        }

        public static bool CheckDLLsPresent(string exePath) {

            bool ret = true;

            foreach (string dll in uevr_dlls) {
                var dllWithPath = Path.Combine(exePath, dll);

                if (!Path.Exists(dllWithPath)) {
                    Logger.Warn($"[dim]{dll}[/] not found");
                    ret = false;
                }
            }

            return ret;
        }

        public static string FileSizeToHumanReadable(long bytes) {
            string suffix;
            double readable;
            switch (Math.Abs(bytes)) {
                case >= 0x1000000000000000:
                    suffix = "EiB";
                    readable = bytes >> 50;
                    break;
                case >= 0x4000000000000:
                    suffix = "PiB";
                    readable = bytes >> 40;
                    break;
                case >= 0x10000000000:
                    suffix = "TiB";
                    readable = bytes >> 30;
                    break;
                case >= 0x40000000:
                    suffix = "GiB";
                    readable = bytes >> 20;
                    break;
                case >= 0x100000:
                    suffix = "MiB";
                    readable = bytes >> 10;
                    break;
                case >= 0x400:
                    suffix = "KiB";
                    readable = bytes;
                    break;
                default:
                    return bytes.ToString("0 B");
            }

            return (readable / 1024).ToString("0.## ", CultureInfo.InvariantCulture) + suffix;
        }

        public static async Task<bool> DownloadUEVRAsync(string downloadURL, string uevrRelease = "") {
            try {
                Logger.Debug($"Downloading UEVR release from: [dim white]{downloadURL}[/]");

                return await AnsiConsole
                                .Progress()
                                .Columns(
                                [
                                    new TaskDescriptionColumn(),
                                    new ProgressBarColumn(),
                                    new PercentageColumn(),
                                    new RemainingTimeColumn(),
                                    new SpinnerColumn(),
                                ])
                                .StartAsync(async ctx => {
                                    var handler = new HttpClientHandler() { AllowAutoRedirect = true };
                                    var ph = new ProgressMessageHandler(handler);

                                    var downloadTask = ctx.AddTask($"Downloading {uevrRelease}");

                                    long lastBytesTransferred = 0;

                                    ph.HttpReceiveProgress += (_, args) => {
                                        Logger.Debug($"Download progress: {args.ProgressPercentage}% {FileSizeToHumanReadable(args.BytesTransferred)}/{FileSizeToHumanReadable(args.TotalBytes ?? 0)}");
                                        downloadTask.MaxValue = args.TotalBytes ?? 0;

                                        var bytesPerTick = args.BytesTransferred - lastBytesTransferred;
                                        lastBytesTransferred = args.BytesTransferred;

                                        downloadTask.Increment(bytesPerTick);
                                    };

                                    using var client = new HttpClient(ph);
                                    using var s = await client.GetStreamAsync(downloadURL);
                                    using var stream = new MemoryStream();
                                    await s.CopyToAsync(stream);
                                    using var zipArchive = new ZipArchive(stream);

                                    downloadTask.StopTask();

                                    var unpackTask = ctx.AddTask($"Unpacking {uevrRelease}");
                                    unpackTask.MaxValue = uevr_dlls.Length;

                                    var filesToUnpack = zipArchive.Entries.Where(entry => uevr_dlls.Contains(entry.Name));
                                    if (filesToUnpack.Count() != uevr_dlls.Length) {
                                        Logger.Error($"Some of required files are missing in the download. [dim]{downloadURL}[/] does not look like valid UEVR release.");
                                        return false;
                                    }

                                    foreach (var entry in filesToUnpack) {
                                        entry.ExtractToFile(Path.Join(Path.GetDirectoryName(Environment.ProcessPath), entry.Name), true);
                                        unpackTask.Increment(1);
                                    }

                                    unpackTask.StopTask();
                                    return true;
                                });

            } catch (Exception e) {
                Logger.Error($"Got exception while downloading: [dim]{e.Message}[/]");
                return false;
            }
        }

        public static async Task<bool> UpdateUEVRAsync(bool forceDownload = false, UEVRBuild uevrBuild = UEVRBuild.Release) {
            try {
                var uevrVersionPath = Path.Join(Path.GetDirectoryName(Environment.ProcessPath), "uevr.version");
                var currentVersion = "";

                try {
                    currentVersion = File.ReadAllLines(uevrVersionPath)[0].Trim().Replace("\n", "");
                } catch (Exception) { }

                var repoOwner = "praydog";
                var repoName = "UEVR";

                if (uevrBuild == UEVRBuild.Nightly) {
                    repoName = "UEVR-nightly";
                }

                var releases = await new GitHubClient(RequestAdapter.Create(new AnonymousAuthenticationProvider())).Repos[repoOwner][repoName].Releases.GetAsync();

                if (releases == null) {
                    Logger.Error($"{repoOwner}/{repoName} responded with empty UEVR releases");
                    return false;
                }
                var latestRelease = releases.First();
                if (latestRelease.Assets == null) {
                    Logger.Error($"Latest {repoOwner}/{repoName} release does not contain any assets");
                    return false;
                }

                if (!forceDownload) {
                    if (currentVersion == "") {
                        Logger.Info($"Manual UEVR installation detected. Auto update won't be performed.");
                    } else {
                        Logger.Debug($"Currently installed version: {currentVersion}");
                    }
                } else {
                    Logger.Debug("Forced update");
                }

                Logger.Debug($"Latest {repoOwner}/{repoName} version: {latestRelease.TagName}");

                // don't update if version file is missing. Allows manual unpacking UEVR to chihuahua directory
                if (forceDownload || ((currentVersion != latestRelease.TagName) && (currentVersion != ""))) {
                    Logger.Info($"Updating {repoOwner}/{repoName} to {latestRelease.TagName}");

                    // nightly release artifact is named in lower case
                    var uevrAssets = latestRelease.Assets.Where(asset => String.Equals(asset.Name, "UEVR.zip", StringComparison.OrdinalIgnoreCase));

                    if (uevrAssets.Count() != 1) {
                        Logger.Error($"[dim]UEVR.zip[/] asset not found in {repoOwner}/{repoName} release {latestRelease.TagName}. Download and unpack files manually.");
                        return false;
                    }

                    var releaseName = string.Format("{0}/{1} {2}", repoOwner, repoName, (uevrBuild == UEVRBuild.Release) ? latestRelease.TagName : latestRelease.TagName?[^6..]);

                    var downloadSucceeded = await DownloadUEVRAsync(uevrAssets.First().BrowserDownloadUrl ?? "", releaseName);

                    if (downloadSucceeded) {
                        File.WriteAllText(uevrVersionPath, latestRelease.TagName + "\n");
                    }

                    return downloadSucceeded;
                }

                Logger.Debug("Skipping UEVR update");
                return true;
            } catch (Exception e) {
                Logger.Error($"Got exception while checking for UEVR releases: [dim]{e.Message}[/]");
                return false;
            }
        }

        [DoesNotReturn]
        public static void WaitForKeyAndExit(int exitCode = 1) {
            Console.ReadKey();
            Environment.Exit(exitCode);
        }

        [DoesNotReturn]
        public static void ExitWithMessage(string message, int exitCode = 1) {
            if (exitCode == 0) {
                Logger.Info(message);
                AnsiConsole.WriteLine("Press any key to exit...");
            } else {
                Logger.Fatal(message);
                AnsiConsole.MarkupLine("[white on red]Error. Press any key to exit...[/]");
            }
            WaitForKeyAndExit(exitCode);
        }

        [DoesNotReturn]
        public static void ExitWithMessage(StatusContext ctx, string message, int exitCode = 1) {
            if (exitCode == 0) {
                Logger.Info(message);
                ctx.Status("Press any key to exit...");
            } else {
                Logger.Fatal(message);
                ctx.Spinner(new Logger.ErrorSpinner());
                ctx.Status("[white on red]Error. Press any key to exit...[/]");
            }

            WaitForKeyAndExit(exitCode);
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
        public static Process[] GetGameProcesses(string gameExe) {
            var processName = Path.GetFileNameWithoutExtension(gameExe);

            return Process.GetProcessesByName(processName);
        }

        // main game process
        public static Process? GetMainGameProcess(string gameExe) {

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
                ExitWithMessage("Failed to inject: [dim]" + dllName + "[/]");
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
                    Logger.Warn("Failed to nullify VR plugins.");
                }
            } else {
                ExitWithMessage("Failed to inject: [dim]UEVRPluginNullifier.dll[/]");
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
                        Logger.Debug("Removed plugin: [dim white]" + pluginDir + "[/]");
                    } catch (Exception e) {
                        Logger.Warn("Failed to remove plugin [dim]" + pluginDir + "[/]. Exception: [dim]" + e.Message + "[/]");
                    }
                }
            }
        }

        public static void TryCloseGame(string gameExe) {
            foreach (var gameProcess in GetGameProcesses(gameExe)) {
                try {
                    gameProcess.Kill();
                } catch (Exception) {
                }
            }
        }

        public static async Task<bool> WaitForGameProcessAsync(string gameExe, int timeoutS) {
            int elapsed = 0;

            Process? gameProcess = null;

            while ((elapsed < timeoutS * 1000) && (gameProcess = GetMainGameProcess(gameExe)) == null) {
                elapsed += 100;
                await Task.Delay(100);
            }

            return gameProcess != null;
        }

        public static async Task WaitBeforeInjectionAsync(StatusContext ctx, int waitTimeS, int timeStepMs = 500) {
            int elapsedMs = 0;
            int waitTimeMs = waitTimeS * 1000;

            while (elapsedMs < waitTimeMs) {
                ctx.Status($"Waiting before injection, [green]{(waitTimeMs - elapsedMs) / 1000}s[/] remaining...");
                elapsedMs += timeStepMs;
                await Task.Delay(timeStepMs);
            }
        }

        public static bool IsUnrealExecutable(string gameExe) {
            var ueSuffixes = ImmutableArray.Create("-WinGDK-Shipping", "-Win64-Shipping");
            var uePathSuffixes = ImmutableArray.Create("\\Binaries\\Win64", "\\Binaries\\WinGDK");

            return ueSuffixes.Any(elem => Path.GetFileNameWithoutExtension(gameExe).EndsWith(elem))
                    || uePathSuffixes.Any(elem => (Path.GetDirectoryName(gameExe) ?? "").EndsWith(elem));
        }

        public static string? TryFindMainExecutable(string gameExe) {
            if (IsUnrealExecutable(gameExe)) return gameExe;

            var exeBaseName = Path.GetFileNameWithoutExtension(gameExe);

            var mainExeGlobs = ImmutableArray.Create(
                                    "*\\Binaries\\Win64\\*-Win64-Shipping.exe",
                                    "*\\Binaries\\WinGDK\\*-WinGDK-Shipping.exe",
                                    $"*\\Binaries\\Win64\\{exeBaseName}.exe",
                                    $"*\\Binaries\\WinGDK\\{exeBaseName}.exe"
            );

            var matcher = new Matcher();
            matcher.AddIncludePatterns(mainExeGlobs);
            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(Path.GetDirectoryName(gameExe) ?? "")));

            if (result.HasMatches) {
                return Path.Join(Path.GetDirectoryName(gameExe), result.Files.First().Path.Replace('/', '\\'));
            }

            return null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
        public static void focusGameWindow(string gameExe) {
            var mainGameProcess = GetMainGameProcess(gameExe);
            if (mainGameProcess == null) {
                Logger.Warn($"Failed to find main game process for {gameExe}");
                return;
            }
            SwitchToThisWindow(mainGameProcess.MainWindowHandle, true);
        }

        private static bool IsProcessRunning(string processName) {
            return Process.GetProcessesByName(processName).Length > 0;
        }

        private static VRSoftware DetectVRSoftware() {
            if (IsProcessRunning("VirtualDesktop.Server")) {
                return VRSoftware.VirtualDesktop;
            }

            if (IsProcessRunning("OculusDash")) {
                return VRSoftware.Oculus;
            }

            if (IsProcessRunning("vrcompositor") || IsProcessRunning("vrserver")) {
                return VRSoftware.SteamVR;
            }

            if (IsProcessRunning("PICO Connect") || IsProcessRunning("Streaming Assistant")) {
                return VRSoftware.Pico;
            }

            return VRSoftware.Unknown;
        }

        private static OpenXRRuntime DetectOpenXRRuntime() {
            try {
                // see https://registry.khronos.org/OpenXR/specs/1.0/loader.html#runtime-discovery
                const string openXRRegistryBase = "Software\\Khronos\\OpenXR";

                string? runtimePath = null;

#pragma warning disable CA1416 // Validate platform compatibility

                // Keys are versioned for example 'Software\Khronos\OpenXR\1' for OpenXR 1.0, find the latest version
                using var openXRRuntimesKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(openXRRegistryBase, false); // false == read only access
                if (openXRRuntimesKey is not null) {
                    var openXRVersions = openXRRuntimesKey?.GetSubKeyNames().Where(key => int.TryParse(key, out _)).Order();

                    if (openXRVersions is not null && openXRVersions.Any()) {
                        var latestOpenXRRuntimeVersionKey = openXRRegistryBase + "\\" + openXRVersions.Last();
                        Logger.Debug($"Found OpenXR runtime registry path: {latestOpenXRRuntimeVersionKey}");

                        using var runtimeRegKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(latestOpenXRRuntimeVersionKey, false);
                        runtimePath = runtimeRegKey?.GetValue("ActiveRuntime") as string;
                    }
                }
#pragma warning restore CA1416 // Validate platform compatibility

                // see https://registry.khronos.org/OpenXR/specs/1.0/loader.html#overriding-the-default-runtime-usage
                var runtimeEnvVar = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON") ?? "";
                if (runtimeEnvVar.Length > 0) {
                    Logger.Debug($"OpenXR ActiveRuntime registry value:\"{runtimePath}\" overwritten with XR_RUNTIME_JSON contents:\"{runtimeEnvVar}\"");
                    runtimePath = runtimeEnvVar;
                }

                if (string.IsNullOrWhiteSpace(runtimePath)) {
                    Logger.Warn("OpenXR runtime path is empty");
                    return OpenXRRuntime.Unknown;
                }

                var runtimeFileName = Path.GetFileName(runtimePath);
                if (runtimeFileName.StartsWith("steamxr", StringComparison.CurrentCultureIgnoreCase)) {
                    return OpenXRRuntime.Steam;
                }

                if (runtimeFileName.StartsWith("virtualdesktop", StringComparison.CurrentCultureIgnoreCase)) {
                    return OpenXRRuntime.VirtualDesktop;
                }

                if (runtimeFileName.StartsWith("oculus", StringComparison.CurrentCultureIgnoreCase)) {
                    return OpenXRRuntime.Oculus;
                }

                if (runtimeFileName.StartsWith("varjo", StringComparison.CurrentCultureIgnoreCase)) {
                    return OpenXRRuntime.Varjo;
                }

                if (runtimeFileName.StartsWith("vive", StringComparison.CurrentCultureIgnoreCase)) {
                    return OpenXRRuntime.ViveVR;
                }

                if (runtimeFileName.StartsWith("mixedrealityruntime", StringComparison.CurrentCultureIgnoreCase)) {
                    return OpenXRRuntime.WMR;
                }

                Logger.Warn($"DetectOpenXRRuntime() unknown runtime definition: {runtimePath}");

            } catch (Exception ex) {
                Logger.Warn($"Got exception when trying to read OpenXR runtime from Registry: {ex.Message}");
            }

            Logger.Debug("DetectOpenXRRuntime() No known OpenXR runtime detected");
            return OpenXRRuntime.Unknown;
        }

        public static RuntimeType DetectRuntimeType() {

            var vrSoftware = DetectVRSoftware();
            var openXRRuntime = DetectOpenXRRuntime();

            Logger.Debug($"DetectRuntimeType() Detected VR software: {vrSoftware}");
            Logger.Debug($"DetectRuntimeType() Detected OpenXR runtime: {openXRRuntime}");

            switch (vrSoftware) {
                case VRSoftware.VirtualDesktop:
                    if (openXRRuntime is not OpenXRRuntime.Steam and not OpenXRRuntime.VirtualDesktop) {
                        Logger.Warn($"Virtual Desktop detected but unsupported OpenXR runtime is active: {openXRRuntime}. Falling back to OpenVR API. This will cause problems.");
                        return RuntimeType.OpenVR;
                    }
                    if (openXRRuntime is not OpenXRRuntime.VirtualDesktop) {
                        Logger.Info("Virtual Desktop detected but VDXR is not active, please fix it in VD streamer options.");
                    }
                    return RuntimeType.OpenXR;

                case VRSoftware.Oculus:
                    if (openXRRuntime == OpenXRRuntime.Oculus) {
                        return RuntimeType.OpenXR;
                    }
                    Logger.Warn($"Oculus Link detected but {openXRRuntime} is active OpenXR runtime. Falling back to OpenVR API.");
                    return RuntimeType.OpenVR;

                case VRSoftware.SteamVR:
                    if (openXRRuntime == OpenXRRuntime.Steam) {
                        return RuntimeType.OpenXR;
                    }
                    Logger.Warn($"SteamVR detected but {openXRRuntime} is active OpenXR runtime. Falling back to OpenVR API.");
                    return RuntimeType.OpenVR;

                case VRSoftware.Pico:
                    if (openXRRuntime == OpenXRRuntime.Steam) {
                        return RuntimeType.OpenXR;
                    }
                    Logger.Warn($"Pico Connect detected but {openXRRuntime} is active OpenXR runtime. Please select SteamVR as OpenXR runtime. Falling back to OpenVR API.");
                    return RuntimeType.OpenVR;

                case VRSoftware.Unknown:
                    if (openXRRuntime != OpenXRRuntime.Unknown) {
                        Logger.Warn($"Last resort. No known VR software detected but OpenXR: \"{openXRRuntime}\" was detected. [yellow bold]Expect problems.[/]");
                        return RuntimeType.OpenXR;
                    }
                    break;

                default:
                    break;
            }

            Logger.Warn("No known VR streamer detected. Is your VR session running?");
            return RuntimeType.Auto;
        }

        public static void InjectRuntime(int mainGameProcessId, RuntimeType runtime = RuntimeType.OpenXR) {
            switch (runtime) {
                case RuntimeType.OpenXR:
                    Logger.Info("Using [dim]OpenXR[/] runtime");
                    InjectDll(mainGameProcessId, "openxr_loader.dll");
                    break;
                case RuntimeType.OpenVR:
                    Logger.Info("Using [dim]OpenVR[/] runtime");
                    InjectDll(mainGameProcessId, "openvr_api.dll");
                    break;
                case RuntimeType.Auto:
                default:
                    ExitWithMessage("Failed to detect runtime type");
                    break;
            }
        }
    }
}

﻿using chiuaua;
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

internal static class Helpers {

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

    public static async Task<bool> DownloadUEVRAsync(string downloadURL, string tagName = "") {
        try {
            Logger.Debug($"Downloading UEVR relase from: [dim white]{downloadURL}[/]");

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

                                var downloadTask = ctx.AddTask($"Downloading UEVR {tagName}");

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

                                var unpackTask = ctx.AddTask($"Unpacking UEVR {tagName}");
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

    public static async Task<bool> CheckUEVRReleaseAsync() {
        try {
            var releases = await new GitHubClient(RequestAdapter.Create(new AnonymousAuthenticationProvider())).Repos["praydog"]["UEVR"].Releases.GetAsync();
            if (releases == null) {
                Logger.Error("Github responded with empty UEVR releases");
                return false;
            }
            var latestRelease = releases.First();
            if (latestRelease.Assets == null) {
                Logger.Error("Latest UEVR release does not contain any assets");
                return false;
            }

            Logger.Debug($"Latest version: {latestRelease.TagName}");

            var uevrAssets = latestRelease.Assets.Where(asset => asset.Name == "UEVR.zip");

            if (uevrAssets.Count() != 1) {
                Logger.Error($"[dim]UEVR.zip[/] asset not found in UEVR release {latestRelease.TagName}. Download and unpack files manually.");
                return false;
            }

            return await DownloadUEVRAsync(uevrAssets.First().BrowserDownloadUrl ?? "", latestRelease.TagName ?? "");
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
        foreach (var gameProcess in GetGameProceses(gameExe)) {
            try {
                gameProcess.Kill();
            } catch (Exception) {
            }
        }
    }

    public static async Task<bool> WaitForGameProcessAsync(string gameExe, int timeoutS) {
        int elapsed = 0;

        Process? gameProcess = null;

        while ((elapsed < timeoutS * 1000) && (gameProcess = GetMainGameProces(gameExe)) == null) {
            elapsed += 100;
            await Task.Delay(100);
        }

        return gameProcess != null;
    }

    public static async Task WaitBeforeInjectionAsync(StatusContext ctx, int waitTimeS, int timeStepMs = 500) {
        int elapsedMs = 0;
        int waitTimeMs = waitTimeS * 1000;

        while (elapsedMs < waitTimeMs) {
            ctx.Status($"Waiting before injection, [green]{(int)((waitTimeMs - elapsedMs) / 1000)}s[/] remaining...");
            elapsedMs += timeStepMs;
            await Task.Delay(timeStepMs);
        }
    }

    public static bool isUnrealExecutable(string gameExe) {
        var ueSuffixes = ImmutableArray.Create("-WinGDK-Shipping", "-Win64-Shipping");

        return ueSuffixes.Any((elem) => Path.GetFileNameWithoutExtension(gameExe).EndsWith(elem));
    }

    public static string? tryFindMainExecutable(string gameExe) {
        if (isUnrealExecutable(gameExe)) return gameExe;

        var mainExeGlobs = ImmutableArray.Create("*\\Binaries\\Win64\\*-Win64-Shipping.exe", "*\\Binaries\\WinGDK\\*-WinGDK-Shipping.exe");

        var matcher = new Matcher();
        matcher.AddIncludePatterns(mainExeGlobs);
        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(Path.GetDirectoryName(gameExe) ?? "")));

        if (result.HasMatches) {
            return Path.Join(Path.GetDirectoryName(gameExe), result.Files.First().Path.Replace('/', '\\'));
        }

        return null;
    }
}
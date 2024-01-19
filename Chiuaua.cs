using System.Diagnostics;
using System.Runtime.InteropServices;

internal class Chiuaua {
    private enum ConsoleCtrlType {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT = 1,
        CTRL_CLOSE_EVENT = 2,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT = 6
    }

    // https://learn.microsoft.com/en-us/windows/console/setconsolectrlhandler?WT.mc_id=DT-MVP-5003978
    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(SetConsoleCtrlEventHandler handler, bool add);

    // https://learn.microsoft.com/en-us/windows/console/handlerroutine?WT.mc_id=DT-MVP-5003978
    private delegate bool SetConsoleCtrlEventHandler(ConsoleCtrlType sig);

    private static string Usage() =>
        "No frills UEVR injector. Chiuaua does what bigger dogs won't."
        + "\n\nUsage: "
        + Process.GetCurrentProcess().ProcessName
        + " --gameExe=game.exe --delay=20"

        + "\n\n\t--gameExe=<full path to game.exe> - Unreal Engine executable to spawn and inject (mandatory)"
        + "\n\t\tTypically game executables are located in \"Game_Code_Name\\Binaries\\Win64\" subfolder and"
        + "\n\t\thave names ending in \"-Win64-Shipping.exe\" or \"-WinGDK-Shipping.exe\""

        + "\n\t--delay=<seconds> - how long to wait before game is injected (optional)"
        + "\n\t--launchCmd=<launcher URI / full .exe path> - launcher command to use (optional)"
        + "\n\t--launchCmdArgs=<space separated list as single string> - launcher command arguments (optional)"

        + "\n\nChiuaua is a good dogo! It will wait for you with console window open in case of errors."
        + "\nIt will also tell you exactly what's going on as it happens."
        + "\n\nClosing console window after injection will force close the game for those stubborn cases that fail to exit."

        + "\n\nExample:"
        + "\nUse Steam to launch Entropy Centre, wait 10s before injection:"
        + "\n\nchiuaua --gameExe=\"D:\\Games\\SteamLibrary\\steamapps\\common\\The Entropy Centre\\Project_Kilo\\Binaries\\Win64\\EntropyCentre-Win64-Shipping.exe\" --launchCmd=\"steam://rungameid/1730590\" --delay=10"

        + "\n\n\nAll this wouldn't be possible without Praydog, author of UEVR mod. All credit goes to him."
        + "\n\t* Please support Praydog on Patreon: https://www.patreon.com/praydog"
        + "\n\t* UEVR Project Website: https://uevr.io/"
        + "\n\t* UEVR Github: https://github.com/praydog/UEVR"
        + "\n\t* Flat2VR Discord : https://discord.com/invite/ZFSCSDe"
        ;

    private static bool ConsoleCloseHandler(ConsoleCtrlType signal) {
        switch (signal) {
            case ConsoleCtrlType.CTRL_BREAK_EVENT:
            case ConsoleCtrlType.CTRL_C_EVENT:
            case ConsoleCtrlType.CTRL_LOGOFF_EVENT:
            case ConsoleCtrlType.CTRL_SHUTDOWN_EVENT:
            case ConsoleCtrlType.CTRL_CLOSE_EVENT:
                Helpers.TryCloseGame(gameExe);
                Environment.Exit(0);
                return false;

            default:
                return false;
        }
    }

    private static string gameExe = "";
    private static string launchCmd = "";
    private static string launchCmdArgs = "";
    private static int injectionDelay = 5000;

    private static void ParseArgs(string[] args) {
        if (args.Length < 2) {
            Helpers.ExitWithMessage(Usage(), 1);
        }

        foreach (var arg in args) {
            if (arg.StartsWith("--gameExe=")) {
                gameExe = arg.Substring(arg.IndexOf("=") + 1);
                continue;
            }
            if (arg.StartsWith("--launchCmd=")) {
                launchCmd = arg.Substring(arg.IndexOf("=") + 1);
                continue;
            }
            if (arg.StartsWith("--launchCmdArgs=")) {
                launchCmdArgs = arg.Substring(arg.IndexOf("=") + 1);
                continue;
            }
            if (arg.StartsWith("--delay=")) {
                try {
                    injectionDelay = int.Parse(arg.Substring(arg.IndexOf("=") + 1)) * 1000;
                } catch (Exception) {
                    Helpers.ExitWithMessage("error parsing --delay=, " + arg.Split("=")[1] + " is not a valid int", 1);
                }
                continue;
            }
        }

        if (gameExe.Length == 0) {
            Helpers.ExitWithMessage("You must specify game executable using --gameExe", 1);
        }

        if (!Path.Exists(gameExe)) {
            Helpers.ExitWithMessage(gameExe + " not found.", 1);
        }

        if (launchCmd.Length == 0) {
            launchCmd = gameExe;
        }

        if (injectionDelay <= 0) {
            Helpers.ExitWithMessage("--delay= argument must be greater than 0", 1);
        }
    }

    private static async Task Main(string[] args) {

        ParseArgs(args);

        var exePath = Path.GetDirectoryName(Environment.ProcessPath);
        if (!Helpers.CheckDLLsPresent(exePath ?? "")) {
            Console.WriteLine("Attempting to download missing files...");
            await Helpers.DownloadUEVRAsync();
        }

        Helpers.RemoveUnwantedPlugins(gameExe);

        Process.Start(new ProcessStartInfo {
            FileName = launchCmd,
            UseShellExecute = true,
            Arguments = launchCmdArgs
        });

        Console.WriteLine("Waiting for {0} to spawn", Path.GetFileName(gameExe));

        if (await Helpers.WaitForGameProcessAsync(gameExe, 30 * 1000)) {
            Console.WriteLine("Game process found");
        } else {
            Helpers.ExitWithMessage("Timed out while waiting for game process", 1);
        }

        Console.WriteLine("Waiting {0}s before injection.", injectionDelay / 1000.0);

        await Task.Delay(injectionDelay);

        var mainGameProcess = Helpers.GetMainGameProces(gameExe);
        if (mainGameProcess == null) {
            Helpers.ExitWithMessage(Path.GetFileName(gameExe) + " exited before it could be injected.", 1);
        }

        Helpers.NullifyPlugins(mainGameProcess.Id);
        Helpers.InjectDll(mainGameProcess.Id, "openxr_loader.dll");
        Helpers.InjectDll(mainGameProcess.Id, "UEVRBackend.dll");

        Console.WriteLine("Injection done, close this window to kill game process.");

        SetConsoleCtrlHandler(ConsoleCloseHandler, true);

        while (Helpers.IsProcessRunning(mainGameProcess.Id)) {
            await Task.Delay(100);
        }

        Console.WriteLine("Game has exitted.");

        if (Helpers.GetGameProceses(gameExe).Length > 0) {
            Console.WriteLine("Leftover game process detected. Terminating...");
            Helpers.TryCloseGame(gameExe);
        }

        Environment.Exit(0);
    }
}
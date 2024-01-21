using NLog;
using NLog.Targets;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.CommandLine.Help;
using Spectre.Console;
using System.Linq;
using Spectre.Console.Rendering;

internal class Chiuaua {
    private enum ConsoleCtrlType {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT = 1,
        CTRL_CLOSE_EVENT = 2,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT = 6
    }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // https://learn.microsoft.com/en-us/windows/console/setconsolectrlhandler?WT.mc_id=DT-MVP-5003978
    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(SetConsoleCtrlEventHandler handler, bool add);

    // https://learn.microsoft.com/en-us/windows/console/handlerroutine?WT.mc_id=DT-MVP-5003978
    private delegate bool SetConsoleCtrlEventHandler(ConsoleCtrlType sig);

    private struct ConsoleCtrlEventArgs {
        public static string gameExe = "";
    }

    private static bool ConsoleCloseHandler(ConsoleCtrlType signal) {
        switch (signal) {
            case ConsoleCtrlType.CTRL_BREAK_EVENT:
            case ConsoleCtrlType.CTRL_C_EVENT:
            case ConsoleCtrlType.CTRL_LOGOFF_EVENT:
            case ConsoleCtrlType.CTRL_SHUTDOWN_EVENT:
            case ConsoleCtrlType.CTRL_CLOSE_EVENT:
                Helpers.TryCloseGame(ConsoleCtrlEventArgs.gameExe);
                Environment.Exit(0);
                return false;

            default:
                return false;
        }
    }

    private static int ParseArgs(string[] args) {
        var delayOption = new Option<int>(name: "--delay", getDefaultValue: () => 5, description: "how long to wait before game is injected");
        var launchCmdOption = new Option<string>(name: "--launch-cmd", description: "launcher command to use.");
        var launchCmdArgsOption = new Option<string>(name: "--launch-args", description: "launcher command arguments, single string, space separated");
        var gameExeArgument = new Argument<FileInfo>(name: "full path to game.exe", description: "Unreal Engine executable to spawn and inject");

        var rootCommand = new RootCommand("No frills UEVR injector. Chiuaua goes where bigger dogs won't.") {
            delayOption,
            launchCmdOption,
            launchCmdArgsOption,
            gameExeArgument,
        };

        rootCommand.SetHandler(async (gameExe, delay, launchCmd, launchCmdArgs) => {
            if (!gameExe.Exists) {
                Helpers.ExitWithMessage($"Game executable: \"{gameExe.FullName}\" not found.");
            }

            await RunAndInject(gameExe.FullName, launchCmd, launchCmdArgs, delay);
        },
        gameExeArgument,
        delayOption,
        launchCmdOption,
        launchCmdArgsOption);

        var parser = new CommandLineBuilder(rootCommand)
                        .UseDefaults()
                        .UseHelp(ctx => {
                            ctx.HelpBuilder.CustomizeSymbol(delayOption, firstColumnText: "--delay <number of seconds>");
                            ctx.HelpBuilder.CustomizeSymbol(launchCmdOption, firstColumnText: "--launch-cmd <launcher URI / full .exe path>");

                            ctx.HelpBuilder.CustomizeLayout(_ => HelpBuilder.Default.GetLayout()
                                .Append(_ => {
                                    AnsiConsole.MarkupLine("Chiuaua is a good dogo!");
                                    AnsiConsole.Write(new Padder(new Markup("It will take care of downloading necessary .dlls, removing pesky VR plugins and streamline whole injection process."
                                                          + "\n\nIn case of any error console window with explanation will wait for you when you exit your game."
                                                          + "If main game process exits it will clean up the leftovers and terminate. So no window afterwards is a good sign."
                                                          + "\n\nClosing console window after injection will force close the game for those stubborn cases that fail to exit."
                                                          )).Padding(2, 0));
                                })
                                .Append(_ => {
                                    AnsiConsole.MarkupLine("\n[bold]Example[/]");
                                    AnsiConsole.Write(new Padder(new Markup("Use Steam to launch Entropy Centre, wait 10s before injection:")).Padding(2, 0));

                                    // prevent splitting to multiple lines
                                    var width = AnsiConsole.Profile.Width;
                                    AnsiConsole.Profile.Width = 400;

                                    AnsiConsole.Write(new Padder(new Markup("[dim]chiuaua \"D:\\Games\\SteamLibrary\\steamapps\\common\\The Entropy Centre\\Project_Kilo\\Binaries\\Win64\\EntropyCentre-Win64-Shipping.exe\" --launch-cmd \"steam://rungameid/1730590\" --delay 10[/]")).Padding(2, 0));

                                    AnsiConsole.Profile.Width = width;

                                })
                                .Append(_ => {
                                    AnsiConsole.MarkupLine("\n[bold]Credits[/]");
                                    AnsiConsole.Write(new Padder(new Markup("All this wouldn't be possible without Praydog, author of UEVR mod. All credit goes to him.")).Padding(2, 0));
                                    AnsiConsole.Write(new Padder(new Markup(
                                                              "* Please support Praydog on Patreon: [blue link]https://www.patreon.com/praydog [/]\n"
                                                            + "* UEVR Project Website: [blue link]https://uevr.io [/]\n"
                                                            + "* UEVR Github: [blue link]https://github.com/praydog/UEVR [/]\n"
                                                            + "* Flat2VR Discord: [blue link]https://discord.com/invite/ZFSCSDe [/]\n"
                                                            )).Padding(4, 0));
                                })
                            );
                        })
                        .Build();

        return parser.Invoke(args);
    }

    private static void SetupLogger() {
        LogManager.Setup().LoadConfiguration(builder => {

            var coloredConsole = new ColoredConsoleTarget() {
                Layout = "${time}|${pad:padding=5:inner=${level:uppercase=true}}|${logger}|${message:withexception=true}",
                RowHighlightingRules = {
                    new ConsoleRowHighlightingRule("level == LogLevel.Debug", ConsoleOutputColor.DarkGray, ConsoleOutputColor.NoChange),
                    new ConsoleRowHighlightingRule("level == LogLevel.Info", ConsoleOutputColor.White, ConsoleOutputColor.NoChange),
                    new ConsoleRowHighlightingRule("level == LogLevel.Warn", ConsoleOutputColor.Yellow, ConsoleOutputColor.NoChange),
                    new ConsoleRowHighlightingRule("level == LogLevel.Error", ConsoleOutputColor.Red, ConsoleOutputColor.NoChange),
                    new ConsoleRowHighlightingRule("level == LogLevel.Fatal", ConsoleOutputColor.Magenta, ConsoleOutputColor.NoChange),
                }
            };

            builder.ForLogger().FilterMinLevel(LogLevel.Info).WriteTo(coloredConsole);
        });
    }

    private static int Main(string[] args) {
        SetupLogger();

        if (ParseArgs(args) != 0) {
            Helpers.ExitWithMessage("Error parsing args.");
        }

        LogManager.Shutdown();
        return 0;
    }

    private static async Task RunAndInject(string gameExe, string? launchCmd, string? launchCmdArgs, int injectionDelayS, int timeoutS = 60) {
        var exePath = Path.GetDirectoryName(Environment.ProcessPath);
        if (!Helpers.CheckDLLsPresent(exePath ?? "")) {
            Logger.Info("Attempting to download missing files...");
            await Helpers.DownloadUEVRAsync();
        }

        Helpers.RemoveUnwantedPlugins(gameExe);

        Process.Start(new ProcessStartInfo {
            FileName = launchCmd ?? gameExe,
            Arguments = launchCmdArgs ?? "",
            UseShellExecute = true,
        });

        Logger.Info("Waiting for {0} to spawn", Path.GetFileName(gameExe));

        if (await Helpers.WaitForGameProcessAsync(gameExe, timeoutS)) {
            Logger.Debug("Game process found");
        } else {
            Helpers.ExitWithMessage("Timed out while waiting for game process");
        }

        Logger.Info("Waiting {0}s before injection.", injectionDelayS);

        await Task.Delay(injectionDelayS * 1000);

        var mainGameProcess = Helpers.GetMainGameProces(gameExe);
        if (mainGameProcess == null) {
            Helpers.ExitWithMessage(Path.GetFileName(gameExe) + " exited before it could be injected.");
        }

        Helpers.NullifyPlugins(mainGameProcess.Id);
        Helpers.InjectDll(mainGameProcess.Id, "openxr_loader.dll");
        Helpers.InjectDll(mainGameProcess.Id, "UEVRBackend.dll");

        Logger.Info("Injection done, close this window to kill game process.");

        ConsoleCtrlEventArgs.gameExe = gameExe;
        SetConsoleCtrlHandler(ConsoleCloseHandler, true);

        while (Helpers.IsProcessRunning(mainGameProcess.Id)) {
            await Task.Delay(100);
        }

        Logger.Info("Game has exitted.");

        if (Helpers.GetGameProceses(gameExe).Length > 0) {
            Logger.Debug("Leftover game process detected. Terminating...");
            Helpers.TryCloseGame(gameExe);
        }
    }
}
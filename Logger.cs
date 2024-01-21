using Spectre.Console;

namespace chiuaua {
    internal static class Logger {

        public static bool verbose = false;

        public sealed class ErrorSpinner : Spinner {
            public override TimeSpan Interval => TimeSpan.FromMilliseconds(250);
            public override bool IsUnicode => false;
            public override IReadOnlyList<string> Frames => [" E", "ER", "RR", "RO", "OR", "R "];
        }

        public static void Log(string prefix, string message) {
            AnsiConsole.MarkupLine(prefix + message);
        }

        public static void Debug(string message) {
            if (verbose) {
                Log("[dim]DEBUG: [/]", $"[gray]{message}[/]");
            }
        }
        public static void Info(string message) {
            Log("[white] INFO: [/]", message);
        }
        public static void Warn(string message) {
            Log("[yellow] WARN: [/]", message);
        }
        public static void Error(string message) {
            Log("[red]ERROR: [/]", message);
        }
        public static void Fatal(string message) {
            Log("[magenta]FATAL: [/]", message);
        }
    }
}

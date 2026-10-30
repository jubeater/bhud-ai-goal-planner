using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Keeps the debug log free of what it may never hold (README, "Diagnosing a Request"): a
    /// Setting's value, the Player's words, or anything a Provider or the GW2 API sent back. An
    /// exception's message can carry any of those, so the code where that data flows logs an
    /// exception by its type name only.
    /// </summary>
    /// <remarks>
    /// This reads the source rather than running it, so it catches the direct forms: reading an
    /// exception's <c>.Message</c>, calling <c>ToString()</c> on one, or handing one whole to a
    /// <c>Logger</c> call. The UI and the module's load and unload keep logging whole exceptions:
    /// they are the last catch for an unexpected error, where the stack trace is the diagnosis.
    /// </remarks>
    public class LogSafetyTests
    {
        private static readonly string[] CheckedFolders = { "Core", "Providers", "World" };

        // Lines that may read an exception's message, each matched by file and a piece of its code,
        // with the reason. Anything else in these files is still checked.
        private static readonly (string File, string Code, string Reason)[] KnownExceptions = {
            (Path.Combine("Providers", "ProbeLoggingChatClient.cs"), "InnerException.Message",
             "logs a Provider's tool-calling refusal after key redaction; #44 trims it to ~200 chars"),
            (Path.Combine("Providers", "ProbeLoggingChatClient.cs"), ": exception.Message",
             "logs a Provider's tool-calling refusal after key redaction; #44 trims it to ~200 chars"),
            (Path.Combine("Providers", "OpenAiCompatibleChatClient.cs"), "\"Error: \" + result.Exception.Message",
             "a failed Lookup's message goes to the model as the tool result, not to the log; #13 lists what is sent"),
            (Path.Combine("World", "Gw2ApiExceptionHelper.cs"), "ex.Message",
             "read only to recognise a 404 after the status code, never logged")
        };

        private static readonly Regex ExceptionMessage =
            new Regex(@"\b\w*(?:ex|Ex|exception|Exception)\.Message\b", RegexOptions.Compiled);

        private static readonly Regex ExceptionToString =
            new Regex(@"\b\w*(?:ex|Ex|exception|Exception)\.ToString\(\)", RegexOptions.Compiled);

        private static readonly Regex WholeExceptionToLogger =
            new Regex(@"\bLogger\.(?:Trace|Debug|Info|Warn|Error|Fatal)\(\s*\w*(?:ex|exception)\s*,", RegexOptions.Compiled);

        [Fact]
        public void Planning_code_logs_exceptions_by_type_only() {
            var offenders = new List<string>();

            foreach (string file in SourceFiles()) {
                string relative = file.Substring(ModuleSourceRoot().Length).TrimStart(Path.DirectorySeparatorChar);

                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++) {
                    string line = lines[i];

                    if (line.TrimStart().StartsWith("//") || IsKnownException(relative, line)) continue;

                    if (ExceptionMessage.IsMatch(line) || ExceptionToString.IsMatch(line) || WholeExceptionToLogger.IsMatch(line)) {
                        offenders.Add(relative + ":" + (i + 1) + ": " + line.Trim());
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                        "Log the exception's type (exception.GetType().Name) instead of its message:\n"
                      + string.Join("\n", offenders));
        }

        [Fact]
        public void Every_known_exception_is_still_in_the_code() {
            foreach (var known in KnownExceptions) {
                string path = Path.Combine(ModuleSourceRoot(), known.File);

                Assert.True(File.Exists(path) && File.ReadAllText(path).Contains(known.Code),
                            known.File + " no longer contains `" + known.Code + "` (" + known.Reason
                          + "); remove it from KnownExceptions.");
            }
        }

        private static bool IsKnownException(string relative, string line) {
            return KnownExceptions.Any(known => string.Equals(known.File, relative, StringComparison.OrdinalIgnoreCase)
                                             && line.Contains(known.Code));
        }

        private static IEnumerable<string> SourceFiles() {
            return CheckedFolders.Select(folder => Path.Combine(ModuleSourceRoot(), folder))
                                 .Where(Directory.Exists)
                                 .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories));
        }

        private static string ModuleSourceRoot() {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiGoalPlanner.sln"))) {
                directory = directory.Parent;
            }

            if (directory == null) throw new InvalidOperationException("AiGoalPlanner.sln not found above " + AppContext.BaseDirectory);

            return Path.Combine(directory.FullName, "src", "AiGoalPlanner");
        }
    }
}

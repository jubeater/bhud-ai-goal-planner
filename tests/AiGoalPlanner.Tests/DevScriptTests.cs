using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the developer channel's PowerShell helpers by running them in isolated processes with
    /// an inbox under the test output — never the real module folder. Each run starts a fresh
    /// PowerShell, so these pin behavior across script invocations, which a single dot-sourced
    /// session cannot.
    /// </summary>
    public class DevScriptTests
    {
        private static readonly string Shell = FindShell();

        [Fact]
        public void Separate_invocations_generate_unique_request_ids() {
            int firstExit, secondExit;
            string inbox;
            string first  = RunDevCommon("New-DevId", "inbox", out inbox, out firstExit);
            string second = RunDevCommon("New-DevId", "inbox", out inbox, out secondExit);

            Assert.Equal(0, firstExit);
            Assert.Equal(0, secondExit);
            Assert.Matches(@"^ps-\d{8}-\d{6}-\d+-\d+$", first);
            Assert.NotEqual(first, second);
        }

        [Theory]
        [InlineData("'0,2'", "0,2")]
        [InlineData("@('0','2')", "0,2")]
        [InlineData("'3'", "3")]
        public void Apply_accepts_the_documented_index_syntax(string given, string expected) {
            int exit;
            string inbox;
            string output = RunDevCommon("((ConvertTo-DevActionIndexes -Actions " + given + ") -join ',')",
                                         "inbox", out inbox, out exit);

            Assert.Equal(0, exit);
            Assert.Equal(expected, output);
        }

        [Fact]
        public void Apply_rejects_a_non_integer_index() {
            int exit;
            string inbox;
            RunDevCommon("ConvertTo-DevActionIndexes -Actions 'x' | Out-Null", "inbox", out inbox, out exit,
                         requireSuccess: false);

            Assert.NotEqual(0, exit);
        }

        [Fact]
        public void Non_send_operations_omit_text() {
            int exit;
            string inbox;
            string json = RunDevCommon("$id = Write-DevRequest -Op 'state'; "
                                       + "Get-Content -Path (Join-Path $script:DevInbox ($id + '.json')) -Raw",
                                       "inbox", out inbox, out exit);

            Assert.Equal(0, exit);
            Assert.Contains("\"op\":\"state\"", json);
            Assert.DoesNotContain("\"text\"", json);
        }

        [Fact]
        public void First_request_creates_the_inbox() {
            int exit;
            string inbox;
            string output = RunDevCommon("$id = Write-DevRequest -Op 'state'; Write-Output $id; "
                                         + "Write-Output (Test-Path (Join-Path $script:DevInbox ($id + '.json')))",
                                         "inbox", out inbox, out exit);

            Assert.Equal(0, exit);

            string[] lines = output.Split(new[] { "\n" }, StringSplitOptions.None);

            Assert.Equal(2, lines.Length);
            Assert.Matches(@"^ps-\d{8}-\d{6}-\d+-\d+$", lines[0].Trim());
            Assert.Equal("True", lines[1].Trim());
        }

        /// <summary>
        /// Runs a snippet with the channel helpers loaded and the inbox pointed at an isolated folder
        /// under the test output. Answers the trimmed standard output and the process exit code, and
        /// removes the folder afterwards.
        /// </summary>
        private static string RunDevCommon(string snippet, string inboxName, out string inboxDir, out int exitCode,
                                             bool requireSuccess = true) {
            string work = Path.Combine(AppContext.BaseDirectory, "dev-probe-" + Guid.NewGuid().ToString("N"));

            inboxDir = Path.Combine(work, inboxName);
            Directory.CreateDirectory(work);

            try {
                string script  = Path.Combine(SolutionDir(), "scripts", "dev-common.ps1");
                string command = ". '" + Escape(script) + "'; $script:DevInbox = '" + Escape(inboxDir) + "'; "
                               + "$script:DevOutbox = '" + Escape(inboxDir + "-outbox") + "'; " + snippet;

                var start = new ProcessStartInfo(Shell, "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"") {
                    UseShellExecute         = false,
                    RedirectStandardOutput  = true,
                    RedirectStandardError   = true
                };

                using (var process = Process.Start(start)) {
                    string output = process.StandardOutput.ReadToEnd();
                    string errors = process.StandardError.ReadToEnd();

                    Assert.True(process.WaitForExit(60000), "PowerShell timed out running: " + snippet);

                    exitCode = process.ExitCode;

                    Assert.True(!requireSuccess || exitCode == 0,
                                "PowerShell failed (" + exitCode + ") running: " + snippet + "\n" + errors);

                    return output.Trim();
                }
            } finally {
                try {
                    Directory.Delete(work, true);
                } catch {
                    // Best effort: a leftover probe folder never affects another test.
                }
            }
        }

        private static string Escape(string path) {
            return path.Replace("'", "''");
        }

        private static string SolutionDir() {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiGoalPlanner.sln"))) {
                directory = directory.Parent;
            }

            if (directory == null) throw new InvalidOperationException("AiGoalPlanner.sln not found above " + AppContext.BaseDirectory);

            return directory.FullName;
        }

        private static string FindShell() {
            foreach (string candidate in new[] { "pwsh", "powershell" }) {
                try {
                    var probe = Process.Start(new ProcessStartInfo(candidate, "-NoProfile -Command \"exit 0\"") {
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true
                    });

                    using (probe) {
                        if (probe.WaitForExit(30000) && probe.ExitCode == 0) return candidate;
                    }
                } catch (System.ComponentModel.Win32Exception) {
                    // Not installed; try the next one.
                }
            }

            throw new InvalidOperationException("No PowerShell (pwsh or powershell) is available to run the script tests.");
        }
    }
}

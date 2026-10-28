using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using Microsoft.Extensions.AI;

// The CI check behind ADR-0008: it runs the real Chat, with the real Lookups on a fake world and
// the real OpenAI-compatible client against a stub Provider, in a process bound to the
// System.Text.Json 5.0 and companion assemblies Blish HUD embeds (see the .csproj target). Paths
// that need a newer System.Text.Json throw MissingMethodException here, exactly as they do inside
// Blish HUD, where unit tests on System.Text.Json 10 cannot see them.
internal static class Program
{
    private static int Main() {
        // The whole point of this check: if the build target's binding redirects did not take
        // effect, everything below would run on the 10.x tree the module compiles against and
        // prove nothing.
        Version systemTextJson = typeof(JsonDocument).Assembly.GetName().Version;
        Console.WriteLine("System.Text.Json loaded: " + systemTextJson);

        if (systemTextJson.Major != 5) {
            Console.WriteLine("FAIL: expected Blish HUD's System.Text.Json 5.0.0.0, not " + systemTextJson + ".");
            return 1;
        }

        int failures = 0;

        failures += Check("The real Lookups build and describe themselves", () => {
            ToolRegistry registry = Lookups.For(new FakeWorld());

            foreach (AIFunctionDeclaration lookup in registry.Declarations) {
                _ = lookup.Name;
                _ = lookup.Description;
                _ = lookup.JsonSchema.GetRawText();
            }
        });

        failures += Check("The real Chat runs both Lookups against a stub Provider",
                          () => RunChat().GetAwaiter().GetResult());

        Console.WriteLine(failures == 0 ? "RESULT: OK" : "RESULT: " + failures + " check(s) failed");

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// A Request through the real Chat, wired the way the module wires it: the stub Provider asks
    /// for both Lookups in turn and then answers, the way a real Provider would drive them.
    /// </summary>
    private static async Task RunChat() {
        int round = 0;

        var provider = new StubProvider(request => {
            round++;

            string body = round == 1
                ? ToolCallReply("call_1", "list_installed_modules", "{}")
                : round == 2
                    ? ToolCallReply("call_2", "get_module_settings", "{\"namespace\":\"bh.pathing\"}")
                    : "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Pathing is installed and shows trails.\"},\"finish_reason\":\"stop\"}]}";

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });

        var client = new OpenAiCompatibleChatClient(ProviderSettings.Parse("https://stub.example/v1", "stub-model", ""),
                                                    new HttpClient(provider));

        var chat = new Chat(() => client, () => "You are AI Goal Planner.", Lookups.For(new FakeWorld()));

        await chat.SendAsync("What does Pathing's trail setting do?");

        if (round != 3) throw new Exception("Expected 3 Provider rounds, got " + round);

        // The list reached the model before it asked for the settings...
        if (!provider.Bodies[1].Contains("bh.pathing")) throw new Exception("The module list did not reach the second Request.");

        // ...the settings came back with their label, value and range...
        if (!provider.Bodies[2].Contains("trail.size") || !provider.Bodies[2].Contains("\\\"value\\\":\\\"3\\\"")) {
            throw new Exception("The settings result did not reach the third Request.");
        }

        // ...and a secret never did, at any point on the wire.
        if (provider.Bodies.Any(body => body.Contains("sk-live-123"))) {
            throw new Exception("A secret reached the Provider.");
        }

        if (chat.Messages.Last().Text != "Pathing is installed and shows trails.") {
            throw new Exception("The final reply did not land in the Chat.");
        }
    }

    private static string ToolCallReply(string callId, string toolName, string arguments) {
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"" + callId
             + "\",\"type\":\"function\",\"function\":{\"name\":\"" + toolName + "\",\"arguments\":\""
             + arguments.Replace("\"", "\\\"") + "\"}}]},\"finish_reason\":\"tool_calls\"}]}";
    }

    private static int Check(string name, Action action) {
        try {
            action();
            Console.WriteLine("PASS  " + name);
            return 0;
        } catch (Exception exception) {
            Exception root = exception;

            while (root.InnerException != null) root = root.InnerException;

            Console.WriteLine("FAIL  " + name);
            Console.WriteLine("      " + root.GetType().Name + ": " + root.Message);
            return 1;
        }
    }

    private sealed class FakeWorld : IBlishHudWorld
    {
        public FakeWorld() {
            Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "Shows markers and trails.", enabled: true));

            SettingsByNamespace["bh.pathing"] = new ModuleSettingsResult(ModuleSettingsStatus.Ok, "Pathing", new[] {
                new ModuleSettingInfo("api_key", "API key", "", "String", "sk-live-123", null, null),
                new ModuleSettingInfo("trail.size", "Trail size", "How wide trails are drawn.", "Int32", "3", "1", "10"),
            });
        }

        public List<InstalledModuleInfo> Modules { get; } = new List<InstalledModuleInfo>();

        private Dictionary<string, ModuleSettingsResult> SettingsByNamespace { get; } =
            new Dictionary<string, ModuleSettingsResult>(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<InstalledModuleInfo>> ListInstalledModulesAsync(CancellationToken cancellationToken) {
            return Task.FromResult<IReadOnlyList<InstalledModuleInfo>>(Modules.ToList());
        }

        public Task<ModuleSettingsResult> GetModuleSettingsAsync(string @namespace, CancellationToken cancellationToken) {
            ModuleSettingsResult result = SettingsByNamespace.TryGetValue(@namespace ?? string.Empty, out ModuleSettingsResult found)
                                              ? found
                                              : new ModuleSettingsResult(ModuleSettingsStatus.ModuleNotInstalled, @namespace ?? string.Empty, new ModuleSettingInfo[0]);

            return Task.FromResult(result);
        }

        public Task<PlayerLocationResult> GetLocationAsync(CancellationToken cancellationToken) {
            return Task.FromResult(new PlayerLocationResult(LocationStatus.Ok, new PlayerLocation("Test Character", 1205, "Domain of Vabbi", 10, "Crystal Desert")));
        }

        public Task<AccountAccessResult> GetAccountAccessAsync(CancellationToken cancellationToken) {
            return Task.FromResult(new AccountAccessResult(AccountAccessStatus.Ok, new[] { "GuildWars2", "HeartOfThorns", "PathOfFire" }));
        }

        public Task<ProgressResult> GetProgressAsync(ProgressKind kind, CancellationToken cancellationToken) {
            return Task.FromResult(new ProgressResult(ProgressStatus.Ok, kind));
        }

        // The check only reads, so a write is never asked for. It answers "not changed" rather than
        // pretending it did something.
        public Task<ModuleChange> SetModuleEnabledAsync(string @namespace, bool enabled, CancellationToken cancellationToken) {
            return Task.FromResult(new ModuleChange(false, false, "This check does not change Modules."));
        }

        public Task<ModuleSettingChange> SetSettingAsync(string @namespace, string key, string value,
                                                         CancellationToken cancellationToken) {
            return Task.FromResult(new ModuleSettingChange(false, null, "This check does not change Settings."));
        }

        public Task<MarkerCategoriesResult> FindMarkerCategoriesAsync(string search, int? mapId, CancellationToken cancellationToken) {
            return Task.FromResult(MarkerCategoriesResult.NoPacks());
        }

        public Task<MarkerPacksResult> ListMarkerPacksAsync(CancellationToken cancellationToken) {
            return Task.FromResult(new MarkerPacksResult(MarkerPacksStatus.Ok, Array.Empty<MarkerPackInfo>()));
        }

        public Task<MarkerPackChange> DownloadMarkerPackAsync(string pack, CancellationToken cancellationToken) {
            return Task.FromResult(new MarkerPackChange(false, false, pack, null, 0, "Stj5Check does not download packs."));
        }

        public Task<MarkerPackChange> DeleteMarkerPackAsync(string fileName, CancellationToken cancellationToken) {
            return Task.FromResult(new MarkerPackChange(false, false, null, fileName, 0, "Stj5Check does not delete packs."));
        }
        public Task<IReadOnlyList<CatalogModuleInfo>> GetCatalogAsync(CancellationToken cancellationToken) {
            return Task.FromResult<IReadOnlyList<CatalogModuleInfo>>(new[] {
                new CatalogModuleInfo("bh.pathing", "Pathing", "3.2.1", "Shows markers and trails.", new[] { "bh.blishhud" }, false)
            });
        }

        public Task<CatalogPackageEntry> GetCatalogPackageEntryAsync(string @namespace, CancellationToken cancellationToken) {
            return Task.FromResult(new CatalogPackageEntry("bh.pathing", "Pathing", "3.2.1", "http://pkg.test/pathing", "HASH"));
        }

        public Task<CatalogModuleDetails> FetchCatalogModuleDetailsAsync(CatalogPackageEntry entry, CancellationToken cancellationToken) {
            return Task.FromResult(new CatalogModuleDetails(true, entry.Namespace, entry.Name, entry.Version, "Shows markers and trails."));
        }

        public Task<CatalogModuleDetails> GetCatalogModuleDetailsAsync(string @namespace, CancellationToken cancellationToken) {
            return Task.FromResult(new CatalogModuleDetails(true, @namespace, "Pathing", "3.2.1", "Shows markers and trails."));
        }

        public Task<ModuleChange> DeleteModuleAsync(string @namespace, CancellationToken cancellationToken) {
            return Task.FromResult(new ModuleChange(true, false, null, changed: true));
        }

        public Task<ModuleInstallChange> InstallModuleAsync(string @namespace, CancellationToken cancellationToken) {
            return Task.FromResult(new ModuleInstallChange(false, false, @namespace, @namespace, null, "Stj5Check does not install modules."));
        }
    }

    private sealed class StubProvider : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubProvider(Func<HttpRequestMessage, HttpResponseMessage> respond) {
            _respond = respond;
        }

        public List<string> Bodies { get; } = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Bodies.Add(await request.Content.ReadAsStringAsync().ConfigureAwait(false));

            return _respond(request);
        }
    }
}

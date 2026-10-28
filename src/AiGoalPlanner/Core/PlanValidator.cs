using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Decides, Action by Action, whether a Plan can be carried out against the Player's real setup,
    /// and says why not when it cannot.
    /// </summary>
    /// <remarks>
    /// The Validator is the last thing between the model's words and the Player's configuration, so
    /// it trusts nothing: a Module is looked up among the Installed Modules, a Setting among the
    /// ones that Module really has, and a value against the Setting's own type and range. Rejected
    /// Actions are kept in the Review with their reason — dropping them silently would leave the
    /// Player wondering why the AI's plan did not do what it said. A Module or key that a plan
    /// touches twice is rejected the second time, so two Actions can never disagree about the same
    /// thing.
    /// </remarks>
    public static class PlanValidator
    {
        /// <summary>
        /// Reviews <paramref name="plan"/> against <paramref name="world"/>, the host that answers
        /// what the Player has and what it is set to. Everything it reads it reads live, on the game
        /// thread, through that world (ADR-0009).
        /// </summary>
        public static async Task<PlanReview> ReviewAsync(Plan plan, IBlishHudWorld world, CancellationToken cancellationToken) {
            if (plan == null)  throw new ArgumentNullException(nameof(plan));
            if (world == null) throw new ArgumentNullException(nameof(world));

            IReadOnlyList<InstalledModuleInfo> modules = await world.ListInstalledModulesAsync(cancellationToken)
                                                                   .ConfigureAwait(false);

            var reviewer = new Reviewer(modules, world, cancellationToken);
            var actions  = new List<ReviewedAction>(plan.Actions.Count);

            foreach (PlanAction action in plan.Actions) {
                actions.Add(await reviewer.ReviewAsync(action).ConfigureAwait(false));
            }

            return new PlanReview(plan, actions);
        }

        /// <summary>
        /// What is wrong with a value for one Setting, or null when that Setting would accept it.
        /// The answer is a phrase that completes "the value cannot be used: …".
        /// </summary>
        /// <remarks>
        /// The rules follow the text form the Lookups hand out (ADR-0008), so a value the model read
        /// from <c>get_module_settings</c> is written back the way it was read: <c>true</c>/<c>false</c>,
        /// an invariant number, an enum's name. A type this module does not know by name is left to
        /// the Module itself, whose refusal comes back from Apply instead of being guessed at here.
        /// </remarks>
        private static string ValueProblem(ModuleSettingInfo setting, string value) {
            string type = setting.TypeName ?? string.Empty;

            switch (type) {
                case "String":  return null;
                case "Boolean": return bool.TryParse(value, out _) ? null : "it has to be true or false";
            }

            if (IsWholeNumber(type)) {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) {
                    return "it has to be a whole number";
                }

                return RangeProblem(setting, number);
            }

            if (IsNumber(type)) {
                if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                                     out double number)) {
                    return "it has to be a number";
                }

                return RangeProblem(setting, number);
            }

            if (setting.AllowedValues.Count > 0) {
                return setting.AllowedValues.Any(allowed => string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase))
                           ? null
                           : "it has to be one of: " + string.Join(", ", setting.AllowedValues);
            }

            return null;
        }

        private static string RangeProblem(ModuleSettingInfo setting, double number) {
            if (IsBeyond(setting.Min, number, isMinimum: true))  return "the smallest allowed value is " + setting.Min;
            if (IsBeyond(setting.Max, number, isMinimum: false)) return "the largest allowed value is " + setting.Max;

            return null;
        }

        private static bool IsBeyond(string bound, double number, bool isMinimum) {
            if (bound == null) return false;
            if (!double.TryParse(bound, NumberStyles.Float, CultureInfo.InvariantCulture, out double limit)) return false;

            return isMinimum ? number < limit : number > limit;
        }

        private static bool IsWholeNumber(string type) {
            switch (type) {
                case "Byte":
                case "SByte":
                case "Int16":
                case "UInt16":
                case "Int32":
                case "UInt32":
                case "Int64":
                case "UInt64":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsNumber(string type) {
            return type == "Single" || type == "Double" || type == "Decimal";
        }

        /// <summary>
        /// One Plan's Review, with what it has to remember while it goes: the Modules it read, the
        /// Settings it already fetched, and the Modules and keys it has already spoken for.
        /// </summary>
        private sealed class Reviewer
        {
            private readonly IReadOnlyList<InstalledModuleInfo>       _modules;
            private readonly IBlishHudWorld                           _world;
            private readonly CancellationToken                        _cancellationToken;
            private readonly Dictionary<string, ModuleSettingsResult> _settings =
                new Dictionary<string, ModuleSettingsResult>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string>                          _moduleStates =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string>                          _settingKeys =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string>                          _downloadPacks =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private MarkerPacksResult                                 _markerPacksResult;

            public Reviewer(IReadOnlyList<InstalledModuleInfo> modules, IBlishHudWorld world,
                            CancellationToken cancellationToken) {
                _modules           = modules ?? new InstalledModuleInfo[0];
                _world             = world;
                _cancellationToken = cancellationToken;
            }

            public async Task<ReviewedAction> ReviewAsync(PlanAction action) {
                if (action.Kind == PlanActionKind.Advice) {
                    return Accept(action, null, null, null, null, null);
                }

                if (action.Kind == PlanActionKind.DownloadPack) {
                    return await ReviewDownloadPackAsync(action).ConfigureAwait(false);
                }

                if (action.Kind == PlanActionKind.Unknown) {
                    string unknown = action.KindText.Length == 0
                                         ? "The AI proposed an action without saying what kind it is."
                                         : "The AI proposed \"" + action.KindText + "\", which this version of AI Goal Planner cannot carry out.";

                    return Reject(action, null, unknown);
                }

                InstalledModuleInfo module = Resolve(action.Module);

                if (module == null) {
                    string missing = action.Module.Length == 0
                                         ? "The action does not say which module it is for."
                                         : "No installed module is called \"" + action.Module + "\".";

                    return Reject(action, null, missing);
                }

                // This module configures the Chat and the Provider; the model that runs inside it
                // has no business changing them, or switching itself off.
                if (string.Equals(module.Namespace, GetModuleSettingsTool.ThisModuleNamespace, StringComparison.OrdinalIgnoreCase)) {
                    return Reject(action, module, "AI Goal Planner's own settings are not something the AI can change.");
                }

                if (action.Kind == PlanActionKind.SetSetting) {
                    return await ReviewSettingAsync(action, module).ConfigureAwait(false);
                }

                if (!_moduleStates.Add(module.Namespace)) {
                    return Reject(action, module, module.Name + " is already changed by another action in this plan.");
                }

                if (action.Kind == PlanActionKind.EnableModule) {
                    return module.Enabled
                               ? Reject(action, module, module.Name + " is already enabled.")
                               : Accept(action, module, null, null, "disabled", "enabled");
                }

                return module.Enabled
                           ? Accept(action, module, null, null, "enabled", "disabled")
                           : Reject(action, module, module.Name + " is already disabled.");
            }

            /// <summary>
            /// One Setting change: the Module has to be enabled for its Settings to be readable, the
            /// key has to be one it really has, and the value has to be one that Setting accepts.
            /// </summary>
            private async Task<ReviewedAction> ReviewSettingAsync(PlanAction action, InstalledModuleInfo module) {
                if (action.Key.Length == 0) {
                    return Reject(action, module, "The action does not say which setting to change.");
                }

                if (!module.Enabled) {
                    return Reject(action, module, module.Name + " is disabled, so its settings cannot be changed. Enable it first.");
                }

                IReadOnlyList<ModuleSettingInfo> settings = await SettingsOfAsync(module).ConfigureAwait(false);

                if (settings == null) {
                    return Reject(action, module, "The settings of " + module.Name + " cannot be read right now.");
                }

                ModuleSettingInfo setting = settings.FirstOrDefault(candidate =>
                                                                       string.Equals(candidate.Key, action.Key, StringComparison.OrdinalIgnoreCase));

                if (setting == null) {
                    return Reject(action, module, module.Name + " has no setting with the key \"" + action.Key + "\".");
                }

                if (!_settingKeys.Add(module.Namespace + "\u001f" + setting.Key)) {
                    return Reject(action, module, "\"" + setting.Key + "\" is already changed by another action in this plan.");
                }

                string problem = ValueProblem(setting, action.Value);

                return problem == null
                           ? Accept(action, module, setting.Key, setting.DisplayName, setting.Value, action.Value)
                           : new ReviewedAction(action, module.Namespace, module.Name, setting.Key, setting.DisplayName,
                                                setting.Value, action.Value,
                                                "The value \"" + action.Value + "\" cannot be used: " + problem + ".");
            }

            /// <summary>
            /// A Module's Settings, read once per Review however many Actions touch it. Null when
            /// they cannot be read at all, which is not the same as a Module that has none.
            /// </summary>
            private async Task<IReadOnlyList<ModuleSettingInfo>> SettingsOfAsync(InstalledModuleInfo module) {
                if (!_settings.TryGetValue(module.Namespace, out ModuleSettingsResult result)) {
                    result = await _world.GetModuleSettingsAsync(module.Namespace, _cancellationToken).ConfigureAwait(false);

                    _settings[module.Namespace] = result;
                }

                return result.Status == ModuleSettingsStatus.Ok ? SettingWalk.Flatten(result.Settings) : null;
            }

            /// <summary>
            /// The Module an Action means: the namespace the Lookups hand out, or the name the
            /// Player sees. A model that answered with "Pathing" instead of "bh.pathing" still gets
            /// the change it proposed.
            /// </summary>
            private async Task<ReviewedAction> ReviewDownloadPackAsync(PlanAction action) {
                string wanted = (action.Pack ?? string.Empty).Trim();

                if (wanted.Length == 0) {
                    return Reject(action, null, "The action does not say which marker pack to download.");
                }

                if (_markerPacksResult == null) {
                    _markerPacksResult = await _world.ListMarkerPacksAsync(_cancellationToken).ConfigureAwait(false);
                }

                MarkerPackInfo pack = _markerPacksResult.Packs?.FirstOrDefault(p =>
                    string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.FileName, wanted, StringComparison.OrdinalIgnoreCase));

                if (pack == null) {
                    return Reject(action, null, "\"" + wanted + "\" is not on Pathing's official marker pack list.");
                }

                if (!_downloadPacks.Add(pack.FileName)) {
                    return Reject(action, null, pack.Name + " is already changed by another action in this plan.");
                }

                if (pack.Installed) {
                    return Reject(action, null, pack.Name + " is already installed.");
                }

                string sizeText = pack.SizeMb > 0
                    ? pack.SizeMb.ToString("0.#", CultureInfo.InvariantCulture) + " MB"
                    : string.Empty;

                return Accept(action, null, null, pack.Name, "not installed", sizeText);
            }

            private InstalledModuleInfo Resolve(string module) {
                string wanted = (module ?? string.Empty).Trim();

                if (wanted.Length == 0) return null;

                return _modules.FirstOrDefault(candidate => string.Equals(candidate.Namespace, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? _modules.FirstOrDefault(candidate => string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase));
            }

            private static ReviewedAction Accept(PlanAction action, InstalledModuleInfo module, string settingKey,
                                                 string targetName, string currentValue, string proposedValue) {
                return new ReviewedAction(action, module?.Namespace, module?.Name, settingKey, targetName,
                                          currentValue, proposedValue, null);
            }

            private static ReviewedAction Reject(PlanAction action, InstalledModuleInfo module, string problem) {
                return new ReviewedAction(action, module?.Namespace, module?.Name, null, null,
                                          module == null ? null : (module.Enabled ? "enabled" : "disabled"),
                                          null, problem);
            }
        }
    }
}



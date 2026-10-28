using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Pkgs;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.World
{
    public sealed partial class BlishHudWorld
    {
        public Task<IReadOnlyList<CatalogModuleInfo>> GetCatalogAsync(CancellationToken cancellationToken) {
            var list = new List<CatalogModuleInfo>();

            try {
                var repoHandler = GameService.Module.ModulePkgRepoHandler;
                if (repoHandler?.PkgRepos != null) {
                    var filters = new Func<PkgManifest, bool>[] { StaticPkgRepoProvider.FilterShowOnlySupportedVersion };

                    foreach (var repo in repoHandler.PkgRepos) {
                        IEnumerable<PkgManifest> manifests = repo.GetPkgManifests(filters);

                        if (manifests != null) {
                            var byNamespace = new Dictionary<string, PkgManifest>(StringComparer.OrdinalIgnoreCase);

                            foreach (PkgManifest m in manifests) {
                                if (string.IsNullOrEmpty(m.Namespace)) continue;

                                if (!byNamespace.TryGetValue(m.Namespace, out PkgManifest current)) {
                                    byNamespace[m.Namespace] = m;
                                } else {
                                    IComparable vCur = VersionOf(current);
                                    IComparable vNew = VersionOf(m);
                                    if (vCur != null && vNew != null && vNew.CompareTo(vCur) > 0) {
                                        byNamespace[m.Namespace] = m;
                                    }
                                }
                            }

                            foreach (PkgManifest m in byNamespace.Values) {
                                var deps = new List<string>();
                                if (m.Dependencies != null) {
                                    foreach (var dep in m.Dependencies) {
                                        if (!string.IsNullOrEmpty(dep.Namespace)) {
                                            deps.Add(dep.Namespace);
                                        }
                                    }
                                }

                                string desc = string.Empty;
                                var apiPerms = new List<string>();

                                ModuleManager installedManager = GameService.Module.Modules
                                    .FirstOrDefault(candidate => string.Equals(candidate.Manifest.Namespace, m.Namespace, StringComparison.OrdinalIgnoreCase));

                                if (installedManager?.Manifest != null) {
                                    desc = installedManager.Manifest.Description ?? string.Empty;
                                    if (installedManager.Manifest.ApiPermissions != null) {
                                        foreach (var kvp in installedManager.Manifest.ApiPermissions) {
                                            apiPerms.Add(kvp.Key.ToString().ToLowerInvariant());
                                        }
                                    }
                                }

                                list.Add(new CatalogModuleInfo(
                                    m.Namespace,
                                    m.Name,
                                    VersionTextOf(m),
                                    desc,
                                    deps,
                                    m.IsPreview,
                                    apiPerms
                                ));
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to load module catalog: " + ex.GetType().Name);
            }

            return Task.FromResult<IReadOnlyList<CatalogModuleInfo>>(list);
        }

        public async Task<ModuleInstallChange> InstallModuleAsync(string @namespace, CancellationToken cancellationToken) {
            string wanted = (@namespace ?? string.Empty).Trim();

            try {
                var repoHandler = GameService.Module.ModulePkgRepoHandler;
                PkgManifest target = null;

                if (repoHandler?.PkgRepos != null) {
                    var filters = new Func<PkgManifest, bool>[] { StaticPkgRepoProvider.FilterShowOnlySupportedVersion };

                    foreach (var repo in repoHandler.PkgRepos) {
                        var manifests = repo.GetPkgManifests(filters);
                        if (manifests != null) {
                            foreach (var m in manifests) {
                                if (string.Equals(m.Namespace, wanted, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(m.Name, wanted, StringComparison.OrdinalIgnoreCase)) {
                                    if (target == null) {
                                        target = m;
                                    } else {
                                        IComparable vCur = VersionOf(target);
                                        IComparable vNew = VersionOf(m);
                                        if (vCur != null && vNew != null && vNew.CompareTo(vCur) > 0) {
                                            target = m;
                                        }
                                    }
                                }
                            }
                        }

                        if (target != null) break;
                    }
                }

                if (target == null) {
                    return new ModuleInstallChange(false, false, wanted, wanted, null, "Module not found in the catalog.");
                }

                string name = target.Name;
                string ns = target.Namespace;
                string version = VersionTextOf(target);

                if (GameService.Module?.Modules != null) {
                    foreach (var module in GameService.Module.Modules) {
                        if (string.Equals(module.Manifest?.Namespace, ns, StringComparison.OrdinalIgnoreCase)) {
                            return new ModuleInstallChange(false, false, name, ns, version, name + " is already installed.");
                        }
                    }
                }

                (ModuleManager NewModule, bool Success, string Error) result = await repoHandler.InstallPackage(target, null, null).ConfigureAwait(false);

                if (!result.Success) {
                    _logWarn?.Invoke("Module failed to install: " + (result.Error != null ? "install error" : "unknown"));
                    return new ModuleInstallChange(false, false, name, ns, version, "Blish HUD could not install " + name + ".");
                }

                return new ModuleInstallChange(true, true, name, ns, version);
            } catch (Exception ex) {
                _logWarn?.Invoke("Module failed to install: " + ex.GetType().Name);
                return new ModuleInstallChange(false, false, wanted, wanted, null, "Blish HUD could not install " + wanted + ".");
            }
        }

        private static IComparable VersionOf(PkgManifest manifest) {
            return typeof(PkgManifest).GetProperty("Version")?.GetValue(manifest, null) as IComparable;
        }

        private static string VersionTextOf(PkgManifest manifest) {
            object version = typeof(PkgManifest).GetProperty("Version")?.GetValue(manifest, null);

            return version?.ToString() ?? string.Empty;
        }
    }
}

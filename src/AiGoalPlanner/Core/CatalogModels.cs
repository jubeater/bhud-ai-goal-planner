using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Information about a module available in the official Blish HUD module catalog.
    /// </summary>
    public sealed class CatalogModuleInfo
    {
        private static readonly IReadOnlyList<string> NoStrings = new string[0];

        public CatalogModuleInfo(
            string @namespace,
            string name,
            string version,
            string description,
            IReadOnlyList<string> dependencies = null,
            bool isPreview = false,
            IReadOnlyList<string> apiPermissions = null) {

            Namespace      = @namespace ?? string.Empty;
            Name           = name ?? string.Empty;
            Version        = version ?? string.Empty;
            Description    = description ?? string.Empty;
            Dependencies   = dependencies ?? NoStrings;
            IsPreview      = isPreview;
            ApiPermissions = apiPermissions ?? NoStrings;
        }

        public string                Namespace      { get; }
        public string                Name           { get; }
        public string                Version        { get; }
        public string                Description    { get; }
        public IReadOnlyList<string> Dependencies   { get; }
        public bool                  IsPreview      { get; }
        public IReadOnlyList<string> ApiPermissions { get; }
    }

    /// <summary>
    /// The outcome of an attempt to install a module from the catalog.
    /// </summary>
    public sealed class ModuleInstallChange
    {
        public ModuleInstallChange(
            bool applied,
            bool changed,
            string moduleName = null,
            string @namespace = null,
            string version = null,
            string problem = null) {

            Applied    = applied;
            Changed    = changed;
            ModuleName = moduleName ?? string.Empty;
            Namespace  = @namespace ?? string.Empty;
            Version    = version ?? string.Empty;
            Problem    = problem ?? string.Empty;
        }

        public bool   Applied    { get; }
        public bool   Changed    { get; }
        public string ModuleName { get; }
        public string Namespace  { get; }
        public string Version    { get; }
        public string Problem    { get; }
    }
}

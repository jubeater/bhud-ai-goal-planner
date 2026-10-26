using System;
using System.Collections.Generic;
using System.Linq;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the walk that flattens a Module's nested Settings for the model: children are qualified
    /// with their parent's key, and whether a Setting is a secret is decided by its own name, so a
    /// nested secret cannot hide behind an empty display name or an innocent parent.
    /// </summary>
    public class SettingWalkTests
    {
        [Fact]
        public void A_nested_Setting_whose_key_reads_as_a_secret_is_hidden_by_its_own_name_alone() {
            var settings = new[] {
                ModuleSettingInfo.Subcollection(
                    "markers", "Markers", "Everything about markers.",
                    new[] {
                        new ModuleSettingInfo("APIKey", "", "", "String", "sk-nested-1", null, null),
                        new ModuleSettingInfo("trail.size", "Trail size", "", "Int32", "3", "1", "10"),
                    })
            };

            IReadOnlyList<ModuleSettingInfo> flat = SettingWalk.Flatten(settings);

            Assert.DoesNotContain(flat, setting => setting.Key == "markers.APIKey");
            Assert.DoesNotContain(flat, setting => setting.Value == "sk-nested-1");
            Assert.Contains(flat, setting => setting.Key == "markers.trail.size");
        }

        [Fact]
        public void A_nested_numbered_secret_is_hidden() {
            var settings = new[] {
                ModuleSettingInfo.Subcollection(
                    "markers", "Markers", "Everything about markers.",
                    new[] {
                        new ModuleSettingInfo("apikey2", "", "", "String", "sk-nested-2", null, null),
                    })
            };

            IReadOnlyList<ModuleSettingInfo> flat = SettingWalk.Flatten(settings);

            Assert.Empty(flat);
        }

        [Fact]
        public void A_subcollection_named_like_a_secret_is_hidden_with_everything_in_it() {
            var settings = new[] {
                ModuleSettingInfo.Subcollection(
                    "credentials", "Credentials", "Accounts for map services.",
                    new[] {
                        new ModuleSettingInfo("show.grid", "Show grid", "", "Boolean", "true", null, null),
                    }),
                ModuleSettingInfo.Subcollection(
                    "markers", "Markers", "Everything about markers.",
                    new[] {
                        new ModuleSettingInfo("show.trails", "Show trails", "", "Boolean", "true", null, null),
                    })
            };

            IReadOnlyList<ModuleSettingInfo> flat = SettingWalk.Flatten(settings);

            Assert.Equal(new[] { "markers.show.trails" }, flat.Select(setting => setting.Key).ToArray());
        }

        [Fact]
        public void A_Setting_is_hidden_when_its_display_name_reads_as_a_secret() {
            var settings = new[] {
                new ModuleSettingInfo("gateway", "Access token for the gateway", "", "String", "hunter2", null, null),
                new ModuleSettingInfo("show.grid", "Show grid", "", "Boolean", "true", null, null),
            };

            IReadOnlyList<ModuleSettingInfo> flat = SettingWalk.Flatten(settings);

            Assert.DoesNotContain(flat, setting => setting.Value == "hunter2");
            Assert.Equal(new[] { "show.grid" }, flat.Select(setting => setting.Key).ToArray());
        }
    }
}

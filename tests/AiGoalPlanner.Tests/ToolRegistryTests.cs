using System;
using System.Linq;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the registry that decides what the AI may call and what each call is: per ADR-0003
    /// every tool is a read-only Lookup except the one that delivers a Plan, which only proposes.
    /// </summary>
    public class ToolRegistryTests
    {
        [Fact]
        public void Every_tool_is_labelled_read_only_or_proposal() {
            var registry = new ToolRegistry();

            registry.Register(ToolAccess.ReadOnly,
                              AIFunctionFactory.Create((Func<Task<string>>)(() => Task.FromResult("[]")), "list_installed_modules", "Lists modules."),
                              arguments => "Checking installed modules…");
            registry.Register(ToolAccess.Proposal,
                              AIFunctionFactory.Create((Func<Task<string>>)(() => Task.FromResult("ok")), "submit_plan", "Delivers a plan."),
                              arguments => "Preparing a plan…");

            Assert.True(registry.TryGet("list_installed_modules", out RegisteredTool readOnly));
            Assert.True(registry.TryGet("submit_plan",            out RegisteredTool proposal));

            Assert.Equal(ToolAccess.ReadOnly, readOnly.Access);
            Assert.Equal(ToolAccess.Proposal, proposal.Access);
        }

        [Fact]
        public void The_model_is_offered_the_tools_by_their_names() {
            var registry = new ToolRegistry();

            registry.Register(ToolAccess.ReadOnly,
                              AIFunctionFactory.Create((Func<Task<string>>)(() => Task.FromResult("[]")), "list_installed_modules", "Lists modules."),
                              arguments => "Checking installed modules…");

            Assert.Equal(new[] { "list_installed_modules" },
                         registry.Declarations.Select(declaration => declaration.Name).ToArray());
        }

        [Fact]
        public void A_name_no_tool_has_is_not_found() {
            var registry = new ToolRegistry();

            Assert.False(registry.TryGet("search_module_catalog", out _));
        }

        [Fact]
        public void A_tool_name_is_found_without_caring_about_its_case() {
            var registry = new ToolRegistry();

            registry.Register(ToolAccess.ReadOnly,
                              AIFunctionFactory.Create((Func<Task<string>>)(() => Task.FromResult("[]")), "list_installed_modules", "Lists modules."),
                              arguments => "Checking installed modules…");

            Assert.True(registry.TryGet("LIST_INSTALLED_MODULES", out RegisteredTool tool));
            Assert.Equal("list_installed_modules", tool.Function.Name);
        }

        [Fact]
        public void Registering_two_tools_under_one_name_is_a_programming_error() {
            var registry = new ToolRegistry();

            registry.Register(ToolAccess.ReadOnly,
                              AIFunctionFactory.Create((Func<Task<string>>)(() => Task.FromResult("[]")), "list_installed_modules", "Lists modules."),
                              arguments => "Checking installed modules…");

            Assert.Throws<ArgumentException>(() => registry.Register(
                                                 ToolAccess.ReadOnly,
                                                 AIFunctionFactory.Create((Func<Task<string>>)(() => Task.FromResult("[]")), "list_installed_modules", "Lists modules again."),
                                                 arguments => "Checking installed modules…"));
        }

        [Fact]
        public void A_progress_line_is_asked_from_the_tool_even_without_arguments() {
            var registry = new ToolRegistry();

            registry.Register(ToolAccess.ReadOnly,
                              AIFunctionFactory.Create((Func<Task<string>>)(() => Task.FromResult("[]")), "list_installed_modules", "Lists modules."),
                              arguments => "Checking installed modules…");

            Assert.True(registry.TryGet("list_installed_modules", out RegisteredTool tool));

            Assert.Equal("Checking installed modules…", tool.ProgressText(null));
        }
    }
}

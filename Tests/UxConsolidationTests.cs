using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class UxConsolidationTests
    {
        [Fact]
        public void EveryLanguage_DefinesConsolidatedNavigationKeys()
        {
            var root = ProjectRoot();
            var languages = new[] { "en", "de", "es", "fr", "pl", "ru", "sk" };
            var keys = new[]
            {
                "plugin_quickssh_title_commandshell_manage",
                "plugin_quickssh_actions_execute_summary",
                "plugin_quickssh_actions_copy_command_title",
                "plugin_quickssh_back_profiles_label",
                "plugin_quickssh_back_profiles_manage_label",
                "plugin_quickssh_back_profiles_selection_label",
                "plugin_quickssh_profiles_remove_confirm",
                "plugin_quickssh_back_actions_label",
                "plugin_quickssh_back_actions_manage_label",
                "plugin_quickssh_back_actions_profile_selection_label",
                "plugin_quickssh_back_actions_action_selection_label",
                "plugin_quickssh_back_keys_label",
                "plugin_quickssh_back_keys_manage_label",
                "plugin_quickssh_back_shell_label",
                "plugin_quickssh_back_shell_manage_label",
                "plugin_quickssh_back_tools_label",
            };

            foreach (var language in languages)
            {
                var text = File.ReadAllText(Path.Combine(
                    root, "Languages", language + ".xaml"));
                foreach (var key in keys)
                    Assert.Contains($"x:Key=\"{key}\"", text);
            }
        }

        [Fact]
        public void SlovakMenu_UsesShortNaturalTitles()
        {
            var text = File.ReadAllText(Path.Combine(
                ProjectRoot(), "Languages", "sk.xaml"));

            Assert.Contains(
                "x:Key=\"plugin_quickssh_title_commandprofiles\">Profily<", text);
            Assert.Contains(
                "x:Key=\"plugin_quickssh_title_commandactions\">Akcie<", text);
            Assert.Contains(
                "x:Key=\"plugin_quickssh_title_commandkeys\">SSH kľúče<", text);
            Assert.Contains(
                "x:Key=\"plugin_quickssh_title_commandshell\">Shell<", text);
            Assert.Contains(
                "x:Key=\"plugin_quickssh_title_commandconfig\">Importovať SSH konfiguráciu<", text);
        }

        [Fact]
        public void BackNavigation_UsesDedicatedHumanLabels()
        {
            var source = File.ReadAllText(Path.Combine(ProjectRoot(), "Main.cs"));

            Assert.Contains("plugin_quickssh_back_profiles_manage_label", source);
            Assert.Contains("plugin_quickssh_back_actions_profile_selection_label", source);
            Assert.Contains("plugin_quickssh_back_actions_action_selection_label", source);
            Assert.Contains("plugin_quickssh_back_keys_manage_label", source);
            Assert.Contains("plugin_quickssh_back_shell_manage_label", source);
            Assert.DoesNotContain("return GetTranslation(\"plugin_quickssh_title_commandprofiles_manage\")", source);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}

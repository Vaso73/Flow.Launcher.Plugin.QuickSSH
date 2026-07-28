using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class FinalUxPolishTests
    {
        [Theory]
        [InlineData("HandleActionsAdd", "HandleActionsRemove", "plugin_quickssh_wizard_actions_add_name_title", "plugin_quickssh_wizard_actions_add_command_title", "plugin_quickssh_actions_save_title")]
        [InlineData("HandleKeysAdd", "HandleKeysInstall", "plugin_quickssh_wizard_keys_add_name_title", "plugin_quickssh_wizard_keys_add_path_title", "plugin_quickssh_keys_save_title")]
        public void AddFlows_GuideNameThenValueAndEndWithExplicitSave(
            string method,
            string nextMethod,
            string firstStepKey,
            string secondStepKey,
            string saveKey)
        {
            var region = ReadMethod(ReadMain(), method, nextMethod);

            Assert.True(Count(region, "MakeWizardExampleResultFromKeys(") >= 2,
                $"{method} must provide two actionable example rows.");
            Assert.Contains(firstStepKey, region);
            Assert.Contains(secondStepKey, region);
            Assert.Contains("AppIconRedPath", region);
            Assert.Contains(saveKey, region);
            Assert.Contains("AppIconGreenPath", region);
            Assert.DoesNotContain("MakeQueryTemplateResult(", region);
        }

        [Fact]
        public void ProfileAdd_GuidesNameServerPortAndAuthenticationBeforeSave()
        {
            var region = ReadMethod(ReadMain(), "HandleProfilesAdd", "HandleProfilesRemove");

            Assert.Contains("plugin_quickssh_wizard_profiles_add_name_title", region);
            Assert.Contains("plugin_quickssh_wizard_profiles_add_target_title", region);
            Assert.Contains("plugin_quickssh_profiles_port_default_title", region);
            Assert.Contains("plugin_quickssh_profiles_port_custom_title", region);
            Assert.Contains("ProfileWizard.PortOption", region);
            Assert.Contains("ProfileWizard.SavedKeyOption", region);
            Assert.Contains("ProfileWizard.DefaultAuthOption", region);
            Assert.Contains("ProfileWizard.IsUsablePrivateKey", region);
            Assert.Contains("plugin_quickssh_profiles_save_title", region);
            Assert.Contains("AppIconGreenPath", region);
        }

        [Fact]
        public void WizardExamples_AreLocalizedActionRows()
        {
            var source = ReadMain();
            var helperStart = source.IndexOf(
                "private Result MakeWizardExampleResultFromKeys(", StringComparison.Ordinal);
            var helperEnd = source.IndexOf(
                "private static string GetBackNavigationLabel(", helperStart, StringComparison.Ordinal);

            Assert.True(helperStart >= 0 && helperEnd > helperStart);
            var helper = source.Substring(helperStart, helperEnd - helperStart);
            Assert.Contains("GetTranslation(titleKey)", helper);
            Assert.Contains("GetTranslation(subtitleKey)", helper);
            Assert.Contains("AutoCompleteText = exampleQuery", helper);
            Assert.Contains("ChangeQuery(exampleQuery, true)", helper);
            Assert.DoesNotContain("private Result MakeWizardStepResult(", source);
        }

        [Fact]
        public void ProfileSelectionViews_UseCompactSharedSubtitle()
        {
            var source = ReadMain();
            var rename = ReadMethod(source, "HandleProfilesRename", "HandleProfilesCopy");
            var copy = ReadMethod(source, "HandleProfilesCopy", "HandleProfilesExport");

            Assert.Contains("BuildProfileListSubtitle(entry.Value)", rename);
            Assert.DoesNotContain("SubTitle = entry.Value?.ToDisplayString()", rename);
            Assert.DoesNotContain("SubTitle = profileValue?.ToDisplayString()", rename);
            Assert.Contains("BuildProfileListSubtitle(entry.Value)", copy);
        }

        [Fact]
        public void SavedKeyRows_UseExplicitPrivateOrPublicLabels()
        {
            var keys = ReadMethod(ReadMain(), "HandleKeysList", "HandleKeysManage");
            Assert.Contains("plugin_quickssh_keys_private_path_label", keys);
            Assert.Contains("plugin_quickssh_keys_public_path_label", keys);
            Assert.Contains("ProfileWizard.GetKeyFileKind(keyEntry)", keys);
            Assert.DoesNotContain("EndsWith(\".pub\", StringComparison.OrdinalIgnoreCase)", keys);
        }

        [Fact]
        public void SlovakLabels_AreShortAndExplainNonDestructiveKeyRemoval()
        {
            var text = File.ReadAllText(Path.Combine(ProjectRoot(), "Languages", "sk.xaml"));

            Assert.Contains("x:Key=\"plugin_quickssh_title_commandshell_add\">Pridať shell<", text);
            Assert.Contains("x:Key=\"plugin_quickssh_title_commandshell_remove\">Odstrániť shell<", text);
            Assert.Contains("x:Key=\"plugin_quickssh_title_commandkeys_install\">Nainštalovať verejný kľúč<", text);
            Assert.Contains("x:Key=\"plugin_quickssh_title_commandkeys_rename\">Premenovať kľúč<", text);
            Assert.Contains("x:Key=\"plugin_quickssh_title_commandkeys_remove\">Odstrániť uložený kľúč<", text);
            Assert.Contains("súbor kľúča zostane zachovaný", text);
            Assert.DoesNotContain("shell profil", text.ToLowerInvariant());
            var lower = text.ToLowerInvariant();
            Assert.DoesNotContain("uložené aliasy", lower);
            Assert.DoesNotContain("neplatný alias", lower);
            Assert.DoesNotContain("alias už existuje", lower);
            Assert.DoesNotContain("alias kľúča", lower);
        }

        [Fact]
        public void EveryLanguage_DefinesFinalUxTranslationKeys()
        {
            var languages = new[] { "en", "de", "es", "fr", "pl", "ru", "sk" };
            var keys = new[]
            {
                "plugin_quickssh_wizard_profiles_add_name_title",
                "plugin_quickssh_wizard_profiles_add_target_title",
                "plugin_quickssh_wizard_profiles_add_auth_title",
                "plugin_quickssh_profiles_port_default_title",
                "plugin_quickssh_profiles_port_custom_title",
                "plugin_quickssh_profiles_port_invalid_title",
                "plugin_quickssh_profiles_key_public_subtitle",
                "plugin_quickssh_profiles_key_invalid_subtitle",
                "plugin_quickssh_profiles_auth_default_title",
                "plugin_quickssh_profiles_auth_advanced_title",
                "plugin_quickssh_wizard_profiles_rename_prefilled_subtitle",
                "plugin_quickssh_wizard_actions_rename_prefilled_subtitle",
                "plugin_quickssh_wizard_keys_rename_prefilled_subtitle",
                "plugin_quickssh_wizard_actions_add_name_title",
                "plugin_quickssh_wizard_actions_add_command_title",
                "plugin_quickssh_wizard_keys_add_name_title",
                "plugin_quickssh_wizard_keys_add_path_title",
                "plugin_quickssh_wizard_shell_add_name_title",
                "plugin_quickssh_wizard_shell_add_command_title",
                "plugin_quickssh_wizard_profiles_rename_title",
                "plugin_quickssh_wizard_actions_rename_title",
                "plugin_quickssh_wizard_keys_rename_title",
                "plugin_quickssh_name_unchanged",
                "plugin_quickssh_keys_private_path_label",
                "plugin_quickssh_keys_public_path_label",
            };

            foreach (var language in languages)
            {
                var text = File.ReadAllText(Path.Combine(
                    ProjectRoot(), "Languages", language + ".xaml"));
                foreach (var key in keys)
                    Assert.Contains($"x:Key=\"{key}\"", text);
            }
        }

        private static int Count(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static string ReadMain()
        {
            return File.ReadAllText(Path.Combine(ProjectRoot(), "Main.cs"));
        }

        private static string ReadMethod(
            string source,
            string methodName,
            string nextMethodName)
        {
            var start = source.IndexOf(
                $"private List<Result> {methodName}(", StringComparison.Ordinal);
            var end = source.IndexOf(
                $"private List<Result> {nextMethodName}(", start, StringComparison.Ordinal);

            Assert.True(start >= 0, $"Method {methodName} was not found.");
            Assert.True(end > start, $"Boundary {nextMethodName} was not found.");
            return source.Substring(start, end - start);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}

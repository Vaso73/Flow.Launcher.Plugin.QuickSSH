using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class WizardUxTests
    {
        [Theory]
        [InlineData("HandleProfilesRename", "HandleProfilesCopy", "plugin_quickssh_wizard_profiles_rename_title", "plugin_quickssh_profiles_rename_confirm_title")]
        [InlineData("HandleActionsRename", "HandleActionsRun", "plugin_quickssh_wizard_actions_rename_title", "plugin_quickssh_actions_rename_confirm_title")]
        [InlineData("HandleKeysRename", "HandleKeysCopyPath", "plugin_quickssh_wizard_keys_rename_title", "plugin_quickssh_keys_rename_confirm_title")]
        public void RenameFlows_GuideTheNewNameAndRequireExplicitConfirmation(
            string method,
            string nextMethod,
            string wizardKey,
            string confirmKey)
        {
            var region = ReadMethod(ReadMain(), method, nextMethod);
            Assert.Contains(wizardKey, region);
            Assert.Contains(confirmKey, region);
            Assert.Contains("ProfileWizard.BuildPrefilledRenameQuery", region);
            Assert.Contains("ProfileWizard.BuildSuggestedName", region);
            Assert.Contains("ProfileWizard.BuildRenameQuery", region);
            Assert.Contains("MakeWizardExampleResult", region);
            Assert.Contains("rename_prefilled_subtitle", region);
            Assert.True(region.Contains("AppIconOrangePath", StringComparison.Ordinal) ||
                        region.Contains("GetSemanticIconPath(\"rename\")", StringComparison.Ordinal));
        }


        [Fact]
        public void AddWizardRows_AreClickableAndUseAvailableExampleNames()
        {
            var source = ReadMain();
            var profiles = ReadMethod(source, "HandleProfilesAdd", "HandleProfilesRename");
            var actions = ReadMethod(source, "HandleActionsAdd", "HandleActionsRemove");
            var keys = ReadMethod(source, "HandleKeysAdd", "HandleKeysInstall");

            Assert.Contains("ProfileWizard.BuildAvailableName(\"server\", profiles.Keys)", profiles);
            Assert.Contains("ProfileWizard.BuildAvailableName(\"check\", actions.Keys)", actions);
            Assert.Contains("ProfileWizard.BuildAvailableName(\"server-key\", keys.Keys)", keys);
            Assert.Contains("ProfileWizard.BuildAvailableName(", source);
            Assert.Contains("\"PowerShell\", _profileManager.UserData.CustomShell.Keys", source);
            Assert.Contains("AutoCompleteText = exampleQuery", source);
            Assert.Contains("ChangeQuery(exampleQuery, true)", source);
        }

        [Fact]
        public void ProfileWizard_OffersPortBeforeAuthenticationAndRejectsPublicKeys()
        {
            var region = ReadMethod(ReadMain(), "HandleProfilesAdd", "HandleProfilesRename");
            Assert.Contains("ProfileWizard.PortOption + \" 22\"", region);
            Assert.Contains("Score = ScoreProfilesWizardDefaultPort", region);
            Assert.Contains("Score = ScoreProfilesWizardCustomPort", region);
            Assert.Contains("ScoreProfilesWizardSavedKeyStart", region);
            Assert.Contains("Score = rowScore", region);
            Assert.Contains("GetProfileKeyUnavailableSubtitle", region);
            Assert.Contains("Score = ScoreProfilesWizardManageKeys", region);
            Assert.Contains("Score = ScoreProfilesWizardDefaultAuth", region);
            Assert.Contains("Score = ScoreProfilesWizardAdvanced", region);
        }

        [Fact]
        public void KeyAdd_RejectsMissingFilesInsteadOfSavingThem()
        {
            var region = ReadMethod(ReadMain(), "HandleKeysAdd", "HandleKeysInstall");
            Assert.Contains("if (!File.Exists(expandedPath))", region);
            Assert.Contains("plugin_quickssh_keys_path_missing_title", region);
            Assert.Contains("return results;", region);
            Assert.Contains("plugin_quickssh_keys_save_title", region);
        }

        [Fact]
        public void ShellAdd_GuidesBothStepsAndKeepsOneTokenCompatibility()
        {
            var source = ReadMain();
            var start = source.IndexOf("case \"add\":", source.IndexOf("private List<Result> HandleShell(", StringComparison.Ordinal), StringComparison.Ordinal);
            var end = source.IndexOf("case \"remove\":", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            var region = source.Substring(start, end - start);

            Assert.Contains("plugin_quickssh_wizard_shell_add_name_title", region);
            Assert.Contains("plugin_quickssh_wizard_shell_add_command_title", region);
            Assert.Contains("plugin_quickssh_wizard_shell_use_name_subtitle", region);
            Assert.Contains("plugin_quickssh_shell_save_title", region);
            Assert.Contains("CustomShell[name] = \"\"", region);
        }

        [Fact]
        public void SlovakWizardText_ExplainsEveryRequiredInput()
        {
            var text = File.ReadAllText(Path.Combine(ProjectRoot(), "Languages", "sk.xaml"));
            Assert.Contains("Doplniť príklad názvu: „{0}“", text);
            Assert.Contains("Doplniť príklad servera: „user@host“", text);
            Assert.Contains("Krok 3 zo 4 • Napíšte číslo od 1 do 65535.", text);
            Assert.Contains("Krok 4 zo 4: Vyberte prihlásenie", text);
            Assert.Contains("Verejný kľúč nemožno použiť na prihlásenie", text);
            Assert.Contains("Doplniť príklad názvu: „{0}“", text);
            Assert.Contains("Doplniť príklad príkazu: „hostname“", text);
            Assert.Contains("Doplniť príklad názvu: „{0}“", text);
            Assert.Contains("Doplniť príklad cesty: „~/.ssh/private_key“", text);
            Assert.Contains("Doplniť návrh nového názvu: „{0}“", text);
        }

        private static string ReadMain()
        {
            return File.ReadAllText(Path.Combine(ProjectRoot(), "Main.cs"));
        }

        private static string ReadMethod(string source, string methodName, string nextMethodName)
        {
            var start = source.IndexOf($"private List<Result> {methodName}(", StringComparison.Ordinal);
            var end = source.IndexOf($"private List<Result> {nextMethodName}(", start, StringComparison.Ordinal);
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

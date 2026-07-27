using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class SubmenuOrderingTests
    {
        [Fact]
        public void Profiles_SavedItemsAreAboveManage()
        {
            Assert.True(QuickSsh.ScoreProfilesSavedItem > QuickSsh.ScoreProfilesActionManage);
        }

        [Fact]
        public void ProfilesManage_DestructiveActionIsLast()
        {
            Assert.True(QuickSsh.ScoreProfilesManageAdd > QuickSsh.ScoreProfilesManageRename);
            Assert.True(QuickSsh.ScoreProfilesManageRename > QuickSsh.ScoreProfilesManageCopy);
            Assert.True(QuickSsh.ScoreProfilesManageCopy > QuickSsh.ScoreProfilesManageExport);
            Assert.True(QuickSsh.ScoreProfilesManageExport > QuickSsh.ScoreProfilesManageImport);
            Assert.True(QuickSsh.ScoreProfilesManageImport > QuickSsh.ScoreProfilesManageRemove);
        }

        [Fact]
        public void Actions_SavedItemsAreAboveManage()
        {
            Assert.True(QuickSsh.ScoreActionsSavedItem > QuickSsh.ScoreActionsActionManage);
            Assert.True(QuickSsh.ScoreActionsManageAdd > QuickSsh.ScoreActionsManageRename);
            Assert.True(QuickSsh.ScoreActionsManageRename > QuickSsh.ScoreActionsManageRemove);
        }

        [Fact]
        public void Shell_SavedItemsAreAboveManage()
        {
            Assert.True(QuickSsh.ScoreShellSelected > QuickSsh.ScoreShellOtherStart);
            Assert.True(QuickSsh.ScoreShellOtherStart > QuickSsh.ScoreShellActionManage);
            Assert.True(QuickSsh.ScoreShellManageAdd > QuickSsh.ScoreShellManageRemove);
        }

        [Fact]
        public void Keys_PrimaryRowsAreAboveManage()
        {
            Assert.True(QuickSsh.ScoreKeysSavedItem > QuickSsh.ScoreKeysActionInstall);
            Assert.True(QuickSsh.ScoreKeysActionInstall > QuickSsh.ScoreKeysActionManage);
        }

        [Fact]
        public void KeysManage_DestructiveActionIsLast()
        {
            Assert.True(QuickSsh.ScoreKeysManageRename > QuickSsh.ScoreKeysManageCopyPath);
            Assert.True(QuickSsh.ScoreKeysManageCopyPath > QuickSsh.ScoreKeysManageCopyPub);
            Assert.True(QuickSsh.ScoreKeysManageCopyPub > QuickSsh.ScoreKeysManageRemove);
        }

        [Fact]
        public void Tools_PrimaryNavigationRowsAreOrdered()
        {
            Assert.True(QuickSsh.ScoreToolsKeys > QuickSsh.ScoreToolsShell);
            Assert.True(QuickSsh.ScoreToolsShell > QuickSsh.ScoreToolsConfig);
        }

        [Fact]
        public void MainSubmenus_HaveNoPassiveHeadingOrAddRow()
        {
            var source = ReadMain();

            var profiles = ReadMethod(source, "HandleProfilesList", "HandleProfilesManage");
            Assert.DoesNotContain("ScoreSubMenuManagement", profiles);
            Assert.DoesNotContain("plugin_quickssh_title_commandprofiles_add", profiles);

            var actions = ReadMethod(source, "HandleActionsList", "HandleActionsManage");
            Assert.DoesNotContain("ScoreSubMenuManagement", actions);
            Assert.DoesNotContain("plugin_quickssh_title_commandactions_add", actions);

            var shell = ReadMethod(source, "HandleShell", "HandleShellManage");
            var shellDefault = shell.IndexOf("default:", StringComparison.Ordinal);
            var shellManage = shell.IndexOf("var manageText", shellDefault, StringComparison.Ordinal);
            Assert.True(shellDefault >= 0 && shellManage > shellDefault);
            Assert.DoesNotContain("plugin_quickssh_noshells",
                shell.Substring(shellDefault, shellManage - shellDefault));

            var keys = ReadMethod(source, "HandleKeysList", "HandleKeysManage");
            Assert.DoesNotContain("ScoreSubMenuManagement", keys);
            Assert.DoesNotContain("plugin_quickssh_title_commandkeys_add", keys);
        }

        [Fact]
        public void ManageSubmenus_ContainAddAndStartWithBack()
        {
            var source = ReadMain();

            AssertManage(source, "HandleProfilesManage", "HandleLegacyAddRedirect",
                "plugin_quickssh_title_commandprofiles_add");
            AssertManage(source, "HandleActionsManage", "HandleActionsUse",
                "plugin_quickssh_title_commandactions_add");
            AssertManage(source, "HandleShellManage", "HandleKeys",
                "plugin_quickssh_title_commandshell_add");
            AssertManage(source, "HandleKeysManage", "HandleKeysAdd",
                "plugin_quickssh_title_commandkeys_add");
        }

        [Fact]
        public void ConfigAndHelp_HaveNoPassiveHeading()
        {
            var source = ReadMain();
            Assert.DoesNotContain("ScoreSubMenuManagement",
                ReadMethod(source, "HandleConfig", "HandleDocs"));
            Assert.DoesNotContain("ScoreSubMenuManagement",
                ReadMethod(source, "HandleDocs", "#endregion"));
        }



        [Fact]
        public void DeepOperationViews_StartWithBackAndHaveNoPassiveHeading()
        {
            var source = ReadMain();
            var methods = new[]
            {
                ("HandleProfilesAdd", "HandleProfilesRemove"),
                ("HandleProfilesRemove", "HandleProfilesRename"),
                ("HandleProfilesRename", "HandleProfilesCopy"),
                ("HandleProfilesCopy", "HandleProfilesExport"),
                ("HandleProfilesExport", "HandleProfilesImport"),
                ("HandleProfilesImport", "HandleDirectConnect"),
                ("HandleActionsUse", "BuildActionConfirmationResults"),
                ("BuildActionConfirmationResults", "HandleActionsAdd"),
                ("HandleActionsAdd", "HandleActionsRemove"),
                ("HandleActionsRemove", "HandleActionsRename"),
                ("HandleActionsRename", "HandleActionsRun"),
                ("HandleActionsRun", "HandleTools"),
                ("HandleShell", "HandleShellManage"),
                ("HandleKeysAdd", "HandleKeysInstall"),
                ("HandleKeysInstall", "HandleKeysGenerate"),
                ("HandleKeysGenerate", "HandleKeysRemove"),
                ("HandleKeysRemove", "HandleKeysRename"),
                ("HandleKeysRename", "HandleKeysCopyPath"),
                ("HandleKeysCopyPath", "HandleKeysCopyPub"),
                ("HandleKeysCopyPub", "HandleKeysScan"),
                ("HandleKeysScan", "HandleConfig"),
            };

            foreach (var (method, nextMethod) in methods)
            {
                var region = ReadMethod(source, method, nextMethod);
                Assert.DoesNotContain("Score = ScoreSubMenuManagement", region);

                var back = region.IndexOf("MakeBackNavResult", StringComparison.Ordinal);
                var firstRow = region.IndexOf("new Result", StringComparison.Ordinal);
                Assert.True(back >= 0, $"{method} must contain back navigation.");
                Assert.True(firstRow < 0 || back < firstRow,
                    $"{method} must place back navigation before every visible row.");
            }
        }

        [Fact]
        public void ActionSelectionAndConfirmation_UseCompactProfileSummaries()
        {
            var source = ReadMain();
            var use = ReadMethod(source, "HandleActionsUse", "BuildActionConfirmationResults");
            Assert.Contains("BuildProfileListSubtitle(entry.Value)", use);
            Assert.DoesNotContain("plugin_quickssh_title_commandactions_run", use);

            var confirmation = ReadMethod(
                source, "BuildActionConfirmationResults", "HandleActionsAdd");
            Assert.Contains("plugin_quickssh_actions_execute_summary", confirmation);
            Assert.Contains("plugin_quickssh_actions_copy_command_title", confirmation);
            Assert.DoesNotContain("plugin_quickssh_actions_profile_label", confirmation);
            Assert.DoesNotContain("plugin_quickssh_actions_action_label", confirmation);
        }

        private static void AssertManage(
            string source, string method, string nextMethod, string addKey)
        {
            var region = ReadMethod(source, method, nextMethod);
            Assert.Contains("MakeBackNavResult", region);
            Assert.Contains(addKey, region);
            Assert.DoesNotContain("ScoreSubMenuManagement", region);
        }

        private static string ReadMain()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, "Main.cs"));
        }

        private static string ReadMethod(
            string source, string methodName, string nextMethodName)
        {
            var start = source.IndexOf(
                $"private List<Result> {methodName}(", StringComparison.Ordinal);
            var end = nextMethodName == "#endregion"
                ? source.IndexOf("#endregion", start, StringComparison.Ordinal)
                : source.IndexOf(
                    $"private List<Result> {nextMethodName}(", start, StringComparison.Ordinal);

            Assert.True(start >= 0, $"Method {methodName} was not found.");
            Assert.True(end > start, $"Boundary {nextMethodName} was not found.");
            return source.Substring(start, end - start);
        }
    }
}

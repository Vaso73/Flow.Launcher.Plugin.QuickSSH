using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ActionsMenuTests
    {
        [Fact]
        public void TopLevelMenu_UsesApprovedSimplifiedOrder()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png")
                .OrderByDescending(r => r.Score)
                .Select(r => r.Title)
                .ToList();

            Assert.Equal(new[] { "profiles", "actions", "tools", "help" }, results);
        }

        [Fact]
        public void ActionsRows_PrioritizeSavedActionsAboveManagement()
        {
            Assert.True(QuickSsh.ScoreBackNavigation > QuickSsh.ScoreActionsSavedItem);
            Assert.True(QuickSsh.ScoreActionsSavedItem > QuickSsh.ScoreActionsActionManage);
            Assert.True(QuickSsh.ScoreActionsManageAdd > QuickSsh.ScoreActionsManageRename);
        }

        [Fact]
        public void Confirmation_PutsBackBeforeRunAndCopy()
        {
            Assert.Equal(int.MaxValue, QuickSsh.ScoreActionsConfirmBack);
            Assert.True(QuickSsh.ScoreActionsConfirmBack > QuickSsh.ScoreActionsConfirmRun);
            Assert.True(QuickSsh.ScoreActionsConfirmRun > QuickSsh.ScoreActionsConfirmCommand);
        }

        [Fact]
        public void SavedAction_OpensActionFirstProfileSelection()
        {
            var mainCsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Main.cs");
            mainCsPath = Path.GetFullPath(mainCsPath);
            if (!File.Exists(mainCsPath))
                return;

            var source = File.ReadAllText(mainCsPath);
            var start = source.IndexOf("private List<Result> HandleActionsList(", StringComparison.Ordinal);
            var end = source.IndexOf("private List<Result> HandleActionsManage(", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);

            var region = source.Substring(start, end - start);
            Assert.Contains("actions use ", region);
            Assert.Contains("Action = _ =>", region);
            Assert.Contains("plugin_quickssh_title_commandactions_manage", region);
            Assert.DoesNotContain("plugin_quickssh_title_commandactions_add", region);
            Assert.DoesNotContain("(\"run\",", region);
        }

        [Fact]
        public void ActionsHandler_ContainsConfirmedExecutionAndNoDevelopmentCopy()
        {
            var mainCsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Main.cs");
            mainCsPath = Path.GetFullPath(mainCsPath);
            if (!File.Exists(mainCsPath))
                return;

            var source = File.ReadAllText(mainCsPath);
            var start = source.IndexOf("private List<Result> HandleActions(", StringComparison.Ordinal);
            var end = source.IndexOf("private List<Result> HandleShell(", start, StringComparison.Ordinal);

            Assert.True(start >= 0 && end > start, "Actions handler region must be present.");
            var actionsRegion = source.Substring(start, end - start);
            Assert.Contains("ActionCommandBuilder.TryBuild", actionsRegion);
            Assert.Contains("ActionCommandBuilder.TryBuildDisplay", actionsRegion);
            Assert.Contains("RunCommand(command)", actionsRegion);
            Assert.Contains("HandleActionsUse", actionsRegion);
            Assert.DoesNotContain("preview only", actionsRegion.ToLowerInvariant());
        }

        [Fact]
        public void ActionsHandler_ProtectsCreateRenameAndRemoveFlows()
        {
            var mainCsPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Main.cs"));
            if (!File.Exists(mainCsPath))
                return;

            var source = File.ReadAllText(mainCsPath);
            Assert.Contains("CommandInputGuard.NormalizeNestedCommandInput", source);
            Assert.Contains("CommandInputGuard.IsReservedSavedName", source);
            Assert.Contains("plugin_quickssh_name_exists", source);
            Assert.Contains("plugin_quickssh_actions_remove_confirm", source);
        }

        [Fact]
        public void ActionsManage_UsesOrangeForRenameAndRedForRemove()
        {
            var mainCsPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Main.cs"));
            if (!File.Exists(mainCsPath))
                return;

            var source = File.ReadAllText(mainCsPath);
            var start = source.IndexOf("private List<Result> HandleActionsManage(", StringComparison.Ordinal);
            var end = source.IndexOf("private List<Result> HandleActionsUse(", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);

            var region = source.Substring(start, end - start);
            Assert.Contains("AppIconGreenPath", region);
            Assert.Contains("AppIconOrangePath", region);
            Assert.Contains("AppIconRedPath", region);
        }

        [Fact]
        public void ActionsRename_UsesOrangeForSelectionAndConfirmation()
        {
            var mainCsPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Main.cs"));
            if (!File.Exists(mainCsPath))
                return;

            var source = File.ReadAllText(mainCsPath);
            var start = source.IndexOf("private List<Result> HandleActionsRename(", StringComparison.Ordinal);
            var end = source.IndexOf("private List<Result> HandleActionsRun(", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);

            var region = source.Substring(start, end - start);
            Assert.True(region.Split("IcoPath = AppIconOrangePath").Length - 1 >= 2);
        }

        [Fact]
        public void ActionConfirmation_StartsWithBackAndHasNoPassiveHeading()
        {
            var mainCsPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Main.cs"));
            if (!File.Exists(mainCsPath))
                return;

            var source = File.ReadAllText(mainCsPath);
            var start = source.IndexOf("private List<Result> BuildActionConfirmationResults(", StringComparison.Ordinal);
            var end = source.IndexOf("private List<Result> HandleActionsAdd(", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);

            var region = source.Substring(start, end - start);
            Assert.DoesNotContain("plugin_quickssh_actions_confirm_title", region);
            Assert.Contains("MakeBackNavResult(query, backQuery, backTarget)", region);
            Assert.Contains("Score = ScoreActionsConfirmRun", region);
            Assert.Contains("plugin_quickssh_actions_copy_command_title", region);
            Assert.DoesNotContain("plugin_quickssh_actions_profile_label", region);
            Assert.DoesNotContain("plugin_quickssh_actions_action_label", region);
        }

        [Fact]
        public void OrangeIcon_IsDeclaredAndPresent()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var mainCsPath = Path.Combine(root, "Main.cs");
            var iconPath = Path.Combine(root, "Images", "app-orange.png");
            if (!File.Exists(mainCsPath))
                return;

            Assert.Contains("AppIconOrangePath", File.ReadAllText(mainCsPath));
            Assert.True(File.Exists(iconPath), "Orange icon must be shipped with the plugin.");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class AutoCompleterTests
    {
        // ── Empty input ───────────────────────────────────────────────────────────

        [Fact]
        public void GetSuggestions_EmptyInput_ReturnsTopLevelCommands()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");

            Assert.NotEmpty(results);
            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            // New top-level commands
            Assert.Contains("profiles", titles);
            Assert.Contains("actions", titles);
            Assert.Contains("tools", titles);
            Assert.Contains("help", titles);
            Assert.DoesNotContain("keys", titles);
            Assert.DoesNotContain("config", titles);
            Assert.DoesNotContain("shell", titles);

            // Removed top-level commands must NOT appear
            Assert.DoesNotContain("add", titles);
            Assert.DoesNotContain("remove", titles);
            Assert.DoesNotContain("export", titles);
            Assert.DoesNotContain("import", titles);
            Assert.DoesNotContain("copy", titles);
            Assert.DoesNotContain("rename", titles);

            // Hidden aliases must NOT appear
            Assert.DoesNotContain("p", titles);
            Assert.DoesNotContain("d", titles);
            Assert.DoesNotContain("docs", titles);
        }

        [Fact]
        public void GetSuggestions_EmptyInput_AutoCompleteTextIncludesTrailingSpace()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");

            foreach (var r in results)
                Assert.True(r.AutoCompleteText?.EndsWith(" "),
                    $"AutoCompleteText for '{r.Title}' should end with a space.");
        }

        // ── Partial input matching ────────────────────────────────────────────────

        [Fact]
        public void GetSuggestions_PartialPr_ReturnsProfiles()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "pr", null, "icon.png");
            Assert.Contains(results, r => r.Title == "profiles");
        }

        [Fact]
        public void GetSuggestions_PartialTo_ReturnsTools()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "to", null, "icon.png");
            Assert.Contains(results, r => r.AutoCompleteText == "ssh tools ");
        }

        [Fact]
        public void GetSuggestions_UnmatchedInput_ReturnsEmptyList()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "zzz", null, "icon.png");
            Assert.Empty(results);
        }

        // ── Profile sub-command suggestions after "profiles " ─────────────────────

        [Fact]
        public void GetSuggestions_ProfilesSpace_SuggestsSubCommands()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "profiles ", null, "icon.png");
            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("manage", titles);
            Assert.Contains("add", titles);
            Assert.Contains("remove", titles);
            Assert.Contains("rename", titles);
            Assert.Contains("copy", titles);
            Assert.Contains("export", titles);
            Assert.Contains("import", titles);
        }

        [Fact]
        public void GetSuggestions_ProfilesPrefix_SuggestsProfileNames()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.Profiles["work"] = new SshProfile { Type = "ssh", HostName = "work.example.com", User = "alice" };
            userData.Profiles["home"] = new SshProfile { Type = "ssh", HostName = "home.example.com", User = "alice" };

            var results = AutoCompleter.GetSuggestions("ssh", "profiles ", userData, "icon.png");

            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("work", titles);
            Assert.Contains("home", titles);
        }

        [Fact]
        public void GetSuggestions_ProfilesPrefixWithSearch_FiltersProfiles()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.Profiles["work"] = new SshProfile { HostName = "work.example.com" };
            userData.Profiles["home"] = new SshProfile { HostName = "home.example.com" };

            var results = AutoCompleter.GetSuggestions("ssh", "profiles wor", userData, "icon.png");

            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("work", titles);
            Assert.DoesNotContain("home", titles);
        }

        // ── Null / missing userData ───────────────────────────────────────────────

        [Fact]
        public void GetSuggestions_NullUserData_DoesNotThrow()
        {
            var exception = Record.Exception(() =>
                AutoCompleter.GetSuggestions("ssh", "profiles ", null, "icon.png"));

            Assert.Null(exception);
        }

        // ── Top-level command order for plain "ssh" ───────────────────────────────

        [Fact]
        public void GetSuggestions_EmptyInput_CommandsHaveDescendingScoresInDefinedOrder()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");

            int profilesScore = results.First(r => r.AutoCompleteText == "ssh profiles ").Score;
            int actionsScore  = results.First(r => r.AutoCompleteText == "ssh actions ").Score;
            int toolsScore    = results.First(r => r.AutoCompleteText == "ssh tools ").Score;
            int helpScore     = results.First(r => r.AutoCompleteText == "ssh help ").Score;

            Assert.True(profilesScore > actionsScore);
            Assert.True(actionsScore > toolsScore);
            Assert.True(toolsScore > helpScore);
        }

        [Fact]
        public void GetSuggestions_EmptyInput_SortedByScoreDescending_YieldsExactOrder()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");
            var ordered = results.OrderByDescending(r => r.Score)
                .Select(r => r.AutoCompleteText)
                .ToList();

            Assert.Equal(new[] { "ssh profiles ", "ssh actions ", "ssh tools ", "ssh help " }, ordered);
        }

        [Fact]
        public void GetSuggestions_EmptyInput_ScoreGapsAreLargeEnoughToResistFuzzyBoost()
        {
            // Flow Launcher's usage-history bonus can add tens of thousands of points
            // for frequently-selected items.  Adjacent top-level command scores must
            // differ by >= 50 000 to prevent runtime reordering.
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");

            var scores = results.OrderByDescending(r => r.Score).Select(r => r.Score).ToList();
            for (int i = 0; i < scores.Count - 1; i++)
            {
                int gap = scores[i] - scores[i + 1];
                Assert.True(gap >= 50_000,
                    $"Score gap between position {i} and {i + 1} is only {gap}; must be >= 50 000.");
            }
        }

        [Fact]
        public void TopLevelScoreConstants_AreInCorrectDescendingOrder()
        {
            Assert.True(QuickSsh.ScoreTopLevelProfiles > QuickSsh.ScoreTopLevelActions);
            Assert.True(QuickSsh.ScoreTopLevelActions > QuickSsh.ScoreTopLevelTools);
            Assert.True(QuickSsh.ScoreTopLevelTools > QuickSsh.ScoreTopLevelHelp);
        }

        [Fact]
        public void TopLevelScoreConstants_GapsAreAtLeast100k()
        {
            int[] scores = new[]
            {
                QuickSsh.ScoreTopLevelProfiles,
                QuickSsh.ScoreTopLevelActions,
                QuickSsh.ScoreTopLevelTools,
                QuickSsh.ScoreTopLevelHelp
            };

            for (int i = 0; i < scores.Length - 1; i++)
                Assert.True(scores[i] - scores[i + 1] >= 100_000);
        }

        // ── Partial "profiles <prefix>" sub-command suggestions ───────────────────

        [Theory]
        [InlineData("a",   new[] { "add" })]
        [InlineData("ad",  new[] { "add" })]
        [InlineData("add", new[] { "add" })]
        [InlineData("r",   new[] { "remove", "rename" })]
        [InlineData("re",  new[] { "remove", "rename" })]
        [InlineData("rem", new[] { "remove" })]
        [InlineData("remo", new[] { "remove" })]
        [InlineData("ren", new[] { "rename" })]
        [InlineData("e",   new[] { "export" })]
        [InlineData("i",   new[] { "import" })]
        [InlineData("c",   new[] { "copy" })]
        public void GetSuggestions_ProfilesPartialPrefix_ShowsMatchingSubCommands(
            string partial, string[] expected)
        {
            var results = AutoCompleter.GetSuggestions("ssh", "profiles " + partial, null, "icon.png");
            var subCommandTitles = results
                .Select(r => r.Title)
                .Where(t => t == "add" || t == "remove" || t == "rename" ||
                            t == "copy" || t == "export" || t == "import")
                .ToHashSet();

            foreach (var e in expected)
                Assert.Contains(e, subCommandTitles);
            Assert.Equal(expected.Length, subCommandTitles.Count);
        }

        [Theory]
        [InlineData("ad",    "remove")]
        [InlineData("ad",    "rename")]
        [InlineData("cop",   "add")]
        [InlineData("expor", "import")]
        public void GetSuggestions_ProfilesPartialSubCommand_DoesNotSuggestNonMatchingSubCommands(
            string partial, string notExpected)
        {
            var results = AutoCompleter.GetSuggestions("ssh", "profiles " + partial, null, "icon.png");
            Assert.DoesNotContain(results, r => r.Title == notExpected);
        }

        [Fact]
        public void GetSuggestions_ProfilesNonSubCommandSearch_StillFiltersProfileNames()
        {
            // "wor" is not a prefix of any sub-command; profile names should still appear.
            var userData = new UserData();
            userData.Attach(() => { });
            userData.Profiles["work"] = new SshProfile { HostName = "work.example.com" };
            userData.Profiles["home"] = new SshProfile { HostName = "home.example.com" };

            var results = AutoCompleter.GetSuggestions("ssh", "profiles wor", userData, "icon.png");

            Assert.Contains(results, r => r.Title == "work");
            Assert.DoesNotContain(results, r => r.Title == "home");
        }

        // ── "actions " sub-command suggestions ───────────────────────────────────

        [Fact]
        public void GetSuggestions_ActionsSpace_SuggestsSubCommandsAndSavedActions()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.CommandProfiles["restart-nginx"] = new CommandProfile { Command = "systemctl restart nginx" };

            var results = AutoCompleter.GetSuggestions("ssh", "actions ", userData, "icon.png");
            var titles = results.Select(r => r.Title).ToHashSet();

            Assert.Contains("run", titles);
            Assert.Contains("add", titles);
            Assert.Contains("manage", titles);
            Assert.Contains("restart-nginx", titles);
            Assert.Contains(results, r => r.Title == "restart-nginx" &&
                r.AutoCompleteText == "ssh actions use restart-nginx ");
        }

        [Fact]
        public void GetSuggestions_ActionsSpaceWithoutSavedActions_OnlySuggestsAdd()
        {
            var userData = new UserData();
            userData.Attach(() => { });

            var results = AutoCompleter.GetSuggestions("ssh", "actions ", userData, "icon.png");
            var titles = results.Select(r => r.Title).ToHashSet();

            Assert.Contains("add", titles);
            Assert.DoesNotContain("run", titles);
            Assert.DoesNotContain("manage", titles);
        }

        [Theory]
        [InlineData("a", new[] { "add" })]
        [InlineData("ru", new[] { "run" })]
        [InlineData("m", new[] { "manage" })]
        public void GetSuggestions_ActionsPartialPrefix_ShowsMatchingSubCommands(
            string partial, string[] expected)
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.CommandProfiles["restart-nginx"] = new CommandProfile { Command = "uptime" };

            var results = AutoCompleter.GetSuggestions(
                "ssh", "actions " + partial, userData, "icon.png");
            var titles = results.Select(r => r.Title)
                .Where(t => t == "run" || t == "add" || t == "manage")
                .ToHashSet();

            foreach (var item in expected)
                Assert.Contains(item, titles);
            Assert.Equal(expected.Length, titles.Count);
        }

        // ── "shell " sub-command suggestions ─────────────────────────────────────

        [Fact]
        public void GetSuggestions_ShellSpace_SuggestsSubCommands()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "shell ", null, "icon.png");
            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("manage", titles);
            Assert.Contains("add", titles);
            Assert.Contains("remove", titles);
        }

        [Theory]
        [InlineData("m",      new[] { "manage" })]
        [InlineData("ma",     new[] { "manage" })]
        [InlineData("a",      new[] { "add" })]
        [InlineData("ad",     new[] { "add" })]
        [InlineData("add",    new[] { "add" })]
        [InlineData("r",      new[] { "remove" })]
        [InlineData("re",     new[] { "remove" })]
        [InlineData("rem",    new[] { "remove" })]
        [InlineData("remo",   new[] { "remove" })]
        [InlineData("remov",  new[] { "remove" })]
        [InlineData("remove", new[] { "remove" })]
        public void GetSuggestions_ShellPartialPrefix_ShowsMatchingSubCommands(
            string partial, string[] expected)
        {
            var results = AutoCompleter.GetSuggestions("ssh", "shell " + partial, null, "icon.png");
            var subCommandTitles = results
                .Select(r => r.Title)
                .Where(t => t == "manage" || t == "add" || t == "remove")
                .ToHashSet();

            foreach (var e in expected)
                Assert.Contains(e, subCommandTitles);
            Assert.Equal(expected.Length, subCommandTitles.Count);
        }

        [Theory]
        [InlineData("ad",    "remove")]
        [InlineData("ad",    "manage")]
        [InlineData("rem",   "add")]
        [InlineData("rem",   "manage")]
        [InlineData("ma",    "add")]
        public void GetSuggestions_ShellPartialSubCommand_DoesNotSuggestNonMatchingSubCommands(
            string partial, string notExpected)
        {
            var results = AutoCompleter.GetSuggestions("ssh", "shell " + partial, null, "icon.png");
            Assert.DoesNotContain(results, r => r.Title == notExpected);
        }

        // ── Exact command match — must return empty (command handler owns the view) ──

        [Theory]
        [InlineData("profiles")]
        [InlineData("actions")]
        [InlineData("tools")]
        [InlineData("keys")]
        [InlineData("config")]
        [InlineData("shell")]
        [InlineData("help")]
        public void GetSuggestions_ExactTopLevelCommandName_NoTrailingSpace_ReturnsEmpty(string exactCommand)
        {
            var results = AutoCompleter.GetSuggestions("ssh", exactCommand, null, "icon.png");
            Assert.Empty(results);
        }

        [Fact]
        public void GetSuggestions_ExactCommandName_CaseInsensitive_ReturnsEmpty()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "PROFILES", null, "icon.png");
            Assert.Empty(results);
        }

        [Fact]
        public void GetSuggestions_ExactCommandName_WithTrailingSpace_IsNotBlocked()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.Profiles["dev"] = new SshProfile { HostName = "dev.host" };

            var results = AutoCompleter.GetSuggestions("ssh", "profiles ", userData, "icon.png");
            Assert.Contains(results, r => r.Title == "dev");
        }

        // ── Partial names just before exact match still return suggestions ─────────

        [Fact]
        public void GetSuggestions_PartialProfilesPrefix_ReturnsSuggestion()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "profi", null, "icon.png");
            Assert.Contains(results, r => r.Title == "profiles");
        }

        // ── AutoCompleteText format ───────────────────────────────────────────────

        [Fact]
        public void GetSuggestions_AutoCompleteText_StartsWithActionKeyword()
        {
            var results = AutoCompleter.GetSuggestions("myssh", "", null, "icon.png");

            foreach (var r in results)
                Assert.True(r.AutoCompleteText?.StartsWith("myssh "),
                    $"AutoCompleteText '{r.AutoCompleteText}' should start with the action keyword.");
        }
    }
}

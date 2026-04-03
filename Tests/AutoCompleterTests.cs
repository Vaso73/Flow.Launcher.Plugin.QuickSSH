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
            Assert.Contains("config", titles);
            Assert.Contains("shell", titles);
            Assert.Contains("help", titles);

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
        public void GetSuggestions_PartialCo_ReturnsConfig()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "co", null, "icon.png");
            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);
            Assert.Contains("config", titles);
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
            // Expected display order: profiles > shell > config > help
            // Flow Launcher sorts by Score descending, so each command must have a
            // strictly higher score than the one that should follow it.
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");

            int profilesScore = results.First(r => r.Title == "profiles").Score;
            int shellScore    = results.First(r => r.Title == "shell").Score;
            int configScore   = results.First(r => r.Title == "config").Score;
            int helpScore     = results.First(r => r.Title == "help").Score;

            Assert.True(profilesScore > shellScore,  "profiles must outrank shell");
            Assert.True(shellScore    > configScore, "shell must outrank config");
            Assert.True(configScore   > helpScore,   "config must outrank help");
        }

        [Fact]
        public void GetSuggestions_EmptyInput_SortedByScoreDescending_YieldsExactOrder()
        {
            // When sorted by Score descending (as Flow Launcher does at runtime),
            // the top-level commands must appear in exactly this order:
            //   1. profiles, 2. shell, 3. config, 4. help
            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");

            var ordered = results.OrderByDescending(r => r.Score).Select(r => r.Title).ToList();

            Assert.Equal(new[] { "profiles", "shell", "config", "help" }, ordered);
        }

        [Fact]
        public void GetSuggestions_EmptyInput_AdjacentScoreGapsExceedFlowLauncherHistoryBonus()
        {
            // Flow Launcher's usage-history bonus can add up to ~1 000 points for a
            // frequently-selected result.  Adjacent top-level commands must be spaced
            // further apart than that so no bonus can reorder them at runtime.
            // A gap of > 1 000 is considered safe based on observed Flow Launcher
            // scoring — this mirrors the "> 500" invariant used for submenu action rows.
            const int safeGap = 1000;

            var results = AutoCompleter.GetSuggestions("ssh", "", null, "icon.png");
            var sorted = results.OrderByDescending(r => r.Score).ToList();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                int gap = sorted[i].Score - sorted[i + 1].Score;
                Assert.True(gap > safeGap,
                    $"Score gap between '{sorted[i].Title}' ({sorted[i].Score}) and " +
                    $"'{sorted[i + 1].Title}' ({sorted[i + 1].Score}) must exceed {safeGap} " +
                    $"to be safe from Flow Launcher usage-history bonuses (actual gap: {gap}).");
            }
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

        // ── "shell " sub-command suggestions ─────────────────────────────────────

        [Fact]
        public void GetSuggestions_ShellSpace_SuggestsSubCommands()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "shell ", null, "icon.png");
            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("add", titles);
            Assert.Contains("remove", titles);
        }

        [Theory]
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
                .Where(t => t == "add" || t == "remove")
                .ToHashSet();

            foreach (var e in expected)
                Assert.Contains(e, subCommandTitles);
            Assert.Equal(expected.Length, subCommandTitles.Count);
        }

        [Theory]
        [InlineData("ad",    "remove")]
        [InlineData("rem",   "add")]
        public void GetSuggestions_ShellPartialSubCommand_DoesNotSuggestNonMatchingSubCommands(
            string partial, string notExpected)
        {
            var results = AutoCompleter.GetSuggestions("ssh", "shell " + partial, null, "icon.png");
            Assert.DoesNotContain(results, r => r.Title == notExpected);
        }

        // ── Exact command match — must return empty (command handler owns the view) ──

        [Theory]
        [InlineData("profiles")]
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

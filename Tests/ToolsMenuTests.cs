using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ToolsMenuTests
    {
        [Fact]
        public void RootMenu_ContainsOnlyProfilesActionsToolsAndHelp()
        {
            var source = ReadSource("AutoCompleter.cs");
            Assert.Contains("\"profiles\", \"actions\", \"tools\", \"help\"", source);
            Assert.DoesNotContain("\"profiles\", \"actions\", \"shell\"", source);
        }

        [Fact]
        public void ToolsMenu_GroupsKeysShellAndConfig()
        {
            var source = ReadSource("Main.cs");
            var start = source.IndexOf("private List<Result> HandleTools(", StringComparison.Ordinal);
            var end = source.IndexOf("private List<Result> HandleShell(", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            var region = source.Substring(start, end - start);
            Assert.Contains("CommandKeys", region);
            Assert.Contains("CommandCustomShell", region);
            Assert.Contains("CommandConfig", region);
            Assert.Contains("MakeBackNavResult", region);
        }

        [Fact]
        public void ProfileList_UsesConciseSubtitle()
        {
            var profile = new SshProfile
            {
                Type = "ssh", User = "vaio", HostName = "dev", Port = "22",
                IdentityFile = @"C:\Users\info\.ssh\private_key"
            };
            Assert.Equal("vaio@dev • private_key", QuickSsh.BuildProfileListSubtitle(profile));
        }

        private static string ReadSource(string name)
        {
            var root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, name));
        }
    }
}

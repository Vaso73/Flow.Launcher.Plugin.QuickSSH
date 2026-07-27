using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class CommandProfileStorageTests : IDisposable
    {
        private readonly string _tmpDir = Path.Combine(
            Path.GetTempPath(), $"quickssh_actions_storage_{Guid.NewGuid():N}");

        public CommandProfileStorageTests() => Directory.CreateDirectory(_tmpDir);

        public void Dispose()
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }

        [Fact]
        public void OldJsonWithoutActions_LoadsWithEmptyCollection()
        {
            var path = Path.Combine(_tmpDir, "profiles.json");
            File.WriteAllText(path, """{"PluginVersion":"2.0","ProfilesLists":{},"CustomShellLists":{},"SshKeysLists":{}}""");

            var pm = new ProfileManager(path);

            Assert.NotNull(pm.UserData.CommandProfiles);
            Assert.Empty(pm.UserData.CommandProfiles);
        }

        [Fact]
        public void AddAction_AutoSavesAndReloads()
        {
            var path = Path.Combine(_tmpDir, "profiles.json");
            var pm = new ProfileManager(path);
            pm.UserData.CommandProfiles["uptime"] = new CommandProfile { Command = "uptime" };

            var reloaded = new ProfileManager(path);

            Assert.True(reloaded.UserData.CommandProfiles.ContainsKey("uptime"));
            Assert.Equal("uptime", reloaded.UserData.CommandProfiles["uptime"].Command);
            Assert.DoesNotContain("RequireConfirmation", File.ReadAllText(path));
        }

        [Fact]
        public void LegacyRequireConfirmation_LoadsAndIsRemovedOnNextSave()
        {
            var path = Path.Combine(_tmpDir, "profiles.json");
            File.WriteAllText(path, """
                {
                  "PluginVersion": "2.0",
                  "ProfilesLists": {},
                  "CustomShellLists": {},
                  "SshKeysLists": {},
                  "CommandProfilesLists": {
                    "uptime": {
                      "Kind": "remote-command",
                      "Command": "uptime",
                      "RequireConfirmation": false
                    }
                  }
                }
                """);

            var pm = new ProfileManager(path);

            Assert.Equal("uptime", pm.UserData.CommandProfiles["uptime"].Command);
            pm.SaveConfiguration();
            Assert.DoesNotContain("RequireConfirmation", File.ReadAllText(path));
        }

        [Fact]
        public void ActionMutation_PreservesProfilesShellsAndKeys()
        {
            var path = Path.Combine(_tmpDir, "profiles.json");
            var pm = new ProfileManager(path);
            pm.UserData.Profiles["server"] = new SshProfile { HostName = "server.example" };
            pm.UserData.CustomShell["pwsh"] = "pwsh.exe";
            pm.UserData.SshKeys["admin"] = new SshKeyEntry { Path = @"C:\keys\admin" };
            pm.UserData.CommandProfiles["uptime"] = new CommandProfile { Command = "uptime" };

            var reloaded = new ProfileManager(path);

            Assert.True(reloaded.UserData.Profiles.ContainsKey("server"));
            Assert.True(reloaded.UserData.CustomShell.ContainsKey("pwsh"));
            Assert.True(reloaded.UserData.SshKeys.ContainsKey("admin"));
            Assert.True(reloaded.UserData.CommandProfiles.ContainsKey("uptime"));
        }
    }
}

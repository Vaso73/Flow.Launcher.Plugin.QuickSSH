using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ProfileStorageTests : IDisposable
    {
        private readonly string _tmpDir;

        public ProfileStorageTests()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), $"quickssh_storage_tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }

        private string GetSettingsDir()
            => Path.Combine(
                _tmpDir,
                "FlowPortable",
                "UserData",
                "Settings",
                "Plugins",
                "Flow.Launcher.Plugin.QuickSSH");

        private string GetLegacyProfilesPath()
            => Path.Combine(_tmpDir, "UserProfile", ".ssh", "profiles.json");

        [Fact]
        public void PrepareProfilesPath_UsesPluginSettingsDirectory()
        {
            var settingsDir = GetSettingsDir();
            var legacyPath = GetLegacyProfilesPath();

            var profilesPath = ProfileStorage.PrepareProfilesPath(settingsDir, legacyPath);

            Assert.Equal(Path.Combine(settingsDir, "profiles.json"), profilesPath);
            Assert.False(File.Exists(profilesPath));
        }

        [Fact]
        public void PrepareProfilesPath_CopiesLegacyFileWhenTargetIsMissing()
        {
            var settingsDir = GetSettingsDir();
            var legacyPath = GetLegacyProfilesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(legacyPath, """{"PluginVersion":"2.0","ProfilesLists":{}}""");

            var profilesPath = ProfileStorage.PrepareProfilesPath(settingsDir, legacyPath);

            Assert.True(File.Exists(profilesPath));
            Assert.Equal(File.ReadAllText(legacyPath), File.ReadAllText(profilesPath));
            Assert.True(File.Exists(legacyPath), "Legacy profiles.json must be preserved.");
            Assert.Empty(Directory.GetFiles(settingsDir, "*.tmp"));
        }

        [Fact]
        public void PrepareProfilesPath_DoesNotOverwriteExistingTarget()
        {
            var settingsDir = GetSettingsDir();
            var profilesPath = Path.Combine(settingsDir, "profiles.json");
            Directory.CreateDirectory(settingsDir);
            File.WriteAllText(profilesPath, """{"PluginVersion":"2.0","ProfilesLists":{"current":{}}}""");

            var legacyPath = GetLegacyProfilesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(legacyPath, """{"PluginVersion":"2.0","ProfilesLists":{"legacy":{}}}""");

            var resolvedPath = ProfileStorage.PrepareProfilesPath(settingsDir, legacyPath);

            Assert.Equal(profilesPath, resolvedPath);
            Assert.Contains("current", File.ReadAllText(profilesPath));
            Assert.DoesNotContain("legacy", File.ReadAllText(profilesPath));
            Assert.Contains("legacy", File.ReadAllText(legacyPath));
        }

        [Fact]
        public void PrepareProfilesPath_CopiedLegacyV1IsMigratedByProfileManager()
        {
            var settingsDir = GetSettingsDir();
            var legacyPath = GetLegacyProfilesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);

            var legacyJson = JsonConvert.SerializeObject(new
            {
                PluginVersion = "1.0",
                EntriesLists = new Dictionary<string, string>
                {
                    ["srv"] = "ssh root@10.0.0.1"
                },
                CustomShellLists = new Dictionary<string, string>()
            }, Formatting.Indented);
            File.WriteAllText(legacyPath, legacyJson);

            var profilesPath = ProfileStorage.PrepareProfilesPath(settingsDir, legacyPath);
            var pm = new ProfileManager(profilesPath);

            Assert.True(pm.UserData.Profiles.ContainsKey("srv"));
            Assert.Equal("root", pm.UserData.Profiles["srv"].User);
            Assert.Equal("10.0.0.1", pm.UserData.Profiles["srv"].HostName);

            var savedJson = File.ReadAllText(profilesPath);
            Assert.DoesNotContain("EntriesLists", savedJson);
            Assert.Contains("ProfilesLists", savedJson);

            Assert.Contains("EntriesLists", File.ReadAllText(legacyPath));
        }
    }
}

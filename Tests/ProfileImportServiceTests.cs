using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ProfileImportServiceTests : IDisposable
    {
        private readonly string _tmpDir;

        public ProfileImportServiceTests()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), $"quickssh_import_tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }

        private ProfileManager CreateManager(out string path)
        {
            path = Path.Combine(_tmpDir, "profiles.json");
            return new ProfileManager(path);
        }

        [Fact]
        public void Import_NewProfile_CreatesBackupAndPersists()
        {
            var manager = CreateManager(out var path);
            manager.UserData.Profiles["existing"] = new SshProfile { HostName = "old.example" };
            var before = File.ReadAllText(path);

            var result = ProfileImportService.Import(
                manager,
                new Dictionary<string, SshProfile>
                {
                    ["new"] = new SshProfile { HostName = "new.example" }
                });

            Assert.Equal(1, result.ImportedCount);
            Assert.Equal(0, result.SkippedCount);
            Assert.Equal(path + ".import.bak", result.BackupPath);
            Assert.Equal(before, File.ReadAllText(result.BackupPath));

            manager.UserData.Profiles["after"] = new SshProfile { HostName = "after.example" };

            var reloaded = new ProfileManager(path);
            Assert.True(reloaded.UserData.Profiles.ContainsKey("existing"));
            Assert.True(reloaded.UserData.Profiles.ContainsKey("new"));
            Assert.True(reloaded.UserData.Profiles.ContainsKey("after"));
        }

        [Fact]
        public void Import_ExistingName_IsSkippedCaseInsensitively()
        {
            var manager = CreateManager(out var path);
            manager.UserData.Profiles["Production"] =
                new SshProfile { HostName = "original.example" };
            var before = File.ReadAllText(path);

            var result = ProfileImportService.Import(
                manager,
                new Dictionary<string, SshProfile>
                {
                    ["production"] = new SshProfile { HostName = "replacement.example" }
                });

            Assert.Equal(0, result.ImportedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal(before, File.ReadAllText(result.BackupPath));
            Assert.Equal("original.example", manager.UserData.Profiles["Production"].HostName);
        }

        [Fact]
        public void Import_SaveFailure_RestoresMemoryAndDisk()
        {
            var manager = CreateManager(out var path);
            manager.UserData.Profiles["existing"] =
                new SshProfile { HostName = "old.example" };
            var before = File.ReadAllText(path);

            Assert.Throws<IOException>(() =>
                ProfileImportService.Import(
                    manager,
                    new Dictionary<string, SshProfile>
                    {
                        ["new"] = new SshProfile { HostName = "new.example" }
                    },
                    () => throw new IOException("Simulated write failure.")));

            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal(before, File.ReadAllText(path + ".import.bak"));
            Assert.True(manager.UserData.Profiles.ContainsKey("existing"));
            Assert.False(manager.UserData.Profiles.ContainsKey("new"));

            manager.UserData.Profiles["after"] = new SshProfile { HostName = "after.example" };

            var reloaded = new ProfileManager(path);
            Assert.True(reloaded.UserData.Profiles.ContainsKey("existing"));
            Assert.False(reloaded.UserData.Profiles.ContainsKey("new"));
            Assert.True(reloaded.UserData.Profiles.ContainsKey("after"));
        }
    }
}

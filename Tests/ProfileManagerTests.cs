using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ProfileManagerTests : IDisposable
    {
        private readonly string _tmpDir;

        public ProfileManagerTests()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), $"quickssh_tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }

        private string GetTmpPath(string filename = "profiles.json")
            => Path.Combine(_tmpDir, filename);

        // ── Creation ──────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_NewFile_CreatesEmptyUserData()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);

            Assert.NotNull(pm.UserData);
            Assert.Empty(pm.UserData.Profiles);
            Assert.Empty(pm.UserData.CustomShell);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Constructor_MissingDirectory_CreatesDirectory()
        {
            var subDir = Path.Combine(_tmpDir, "sub", "dir");
            var path = Path.Combine(subDir, "profiles.json");

            var pm = new ProfileManager(path);

            Assert.True(Directory.Exists(subDir));
            Assert.True(File.Exists(path));
        }

        // ── Save & Load ───────────────────────────────────────────────────────────

        [Fact]
        public void SaveConfiguration_WritesValidJson()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);
            pm.UserData.Profiles["work"] = new SshProfile { Type = "ssh", User = "alice", HostName = "work.example.com" };

            pm.SaveConfiguration();

            var json = File.ReadAllText(path);
            Assert.Contains("work", json);
            Assert.Contains("alice", json);
        }

        [Fact]
        public void LoadConfiguration_ReadsPersistedProfiles()
        {
            var path = GetTmpPath();
            var pm1 = new ProfileManager(path);
            pm1.UserData.Profiles["myhost"] = new SshProfile { Type = "ssh", User = "root", HostName = "myhost" };
            pm1.SaveConfiguration();

            var pm2 = new ProfileManager(path);

            Assert.True(pm2.UserData.Profiles.ContainsKey("myhost"));
            Assert.Equal("myhost", pm2.UserData.Profiles["myhost"].HostName);
        }

        [Fact]
        public void AutoSave_TriggeredOnProfileAdd()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);

            pm.UserData.Profiles["autokey"] = new SshProfile { HostName = "auto.host" };

            var pm2 = new ProfileManager(path);
            Assert.True(pm2.UserData.Profiles.ContainsKey("autokey"));
        }

        [Fact]
        public void AutoSave_TriggeredOnProfileRemove()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);
            pm.UserData.Profiles["removeme"] = new SshProfile { HostName = "host" };

            pm.UserData.Profiles.Remove("removeme");

            var pm2 = new ProfileManager(path);
            Assert.False(pm2.UserData.Profiles.ContainsKey("removeme"));
        }

        [Fact]
        public void SaveConfiguration_UsesAtomicWrite()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);
            pm.UserData.Profiles["k"] = new SshProfile { HostName = "v" };
            pm.SaveConfiguration();

            Assert.False(File.Exists(path + ".tmp"),
                "Temp file should be cleaned up after atomic move.");
        }

        // ── Legacy migration ──────────────────────────────────────────────────────

        [Fact]
        public void LoadConfiguration_V1RawStrings_MigratedToStructuredProfiles()
        {
            var path = GetTmpPath();

            // Write a v1-style JSON
            var v1Json = JsonConvert.SerializeObject(new
            {
                PluginVersion = "1.0",
                EntriesLists = new Dictionary<string, string>
                {
                    ["myserver"] = "ssh user@myhost",
                    ["dev-box"]  = "ssh -p 2222 dev@10.0.0.50"
                },
                CustomShellLists = new Dictionary<string, string>()
            }, Formatting.Indented);
            File.WriteAllText(path, v1Json);

            var pm = new ProfileManager(path);

            Assert.True(pm.UserData.Profiles.ContainsKey("myserver"), "myserver should be migrated");
            Assert.True(pm.UserData.Profiles.ContainsKey("dev-box"), "dev-box should be migrated");

            var myserver = pm.UserData.Profiles["myserver"];
            Assert.Equal("user", myserver.User);
            Assert.Equal("myhost", myserver.HostName);

            var devbox = pm.UserData.Profiles["dev-box"];
            Assert.Equal("2222", devbox.Port);
            Assert.Equal("dev", devbox.User);
        }

        [Fact]
        public void LoadConfiguration_V1Migration_SavesAsV2Format()
        {
            var path = GetTmpPath();

            var v1Json = JsonConvert.SerializeObject(new
            {
                PluginVersion = "1.0",
                EntriesLists = new Dictionary<string, string> { ["srv"] = "ssh root@10.0.0.1" },
                CustomShellLists = new Dictionary<string, string>()
            }, Formatting.Indented);
            File.WriteAllText(path, v1Json);

            // Load must trigger migration AND immediately persist the v2 format to disk.
            _ = new ProfileManager(path);

            // Verify the on-disk JSON no longer contains the legacy raw-string field.
            var savedJson = File.ReadAllText(path);
            Assert.DoesNotContain("EntriesLists", savedJson);

            // Verify the v2 structured field IS present.
            Assert.Contains("ProfilesLists", savedJson);

            // Reload and verify structured format is readable.
            var pm2 = new ProfileManager(path);
            Assert.True(pm2.UserData.Profiles.ContainsKey("srv"));
            Assert.Equal("root", pm2.UserData.Profiles["srv"].User);
        }

        [Fact]
        public void LoadConfiguration_V1Migration_ImmediateSave_PreventsReMigrationOnReload()
        {
            var path = GetTmpPath();

            var v1Json = JsonConvert.SerializeObject(new
            {
                PluginVersion = "1.0",
                EntriesLists = new Dictionary<string, string> { ["box"] = "ssh deploy@10.0.0.5" },
                CustomShellLists = new Dictionary<string, string>()
            }, Formatting.Indented);
            File.WriteAllText(path, v1Json);

            // First load: migration runs and saves.
            _ = new ProfileManager(path);

            // Second load: file is already v2, no further migration should occur.
            // Verify the profile is still correct after a second round-trip.
            var pm2 = new ProfileManager(path);
            Assert.True(pm2.UserData.Profiles.ContainsKey("box"));
            Assert.Equal("deploy", pm2.UserData.Profiles["box"].User);
            Assert.Equal("10.0.0.5", pm2.UserData.Profiles["box"].HostName);
        }

        [Fact]
        public void LoadConfiguration_V1Migration_UnknownFlags_PreservedInExtraArgs()
        {
            // Profiles with flags that cannot be represented in structured fields must survive
            // migration without silent data loss.  Unknown flags are stored in ExtraArgs.
            var path = GetTmpPath();

            var v1Json = JsonConvert.SerializeObject(new
            {
                PluginVersion = "1.0",
                EntriesLists = new Dictionary<string, string>
                {
                    ["x11"] = "ssh -X root@x11host",          // -X (X11 forwarding) is unstructured
                    ["agent"] = "ssh -A root@agenthost"       // -A (agent forwarding) is unstructured
                },
                CustomShellLists = new Dictionary<string, string>()
            }, Formatting.Indented);
            File.WriteAllText(path, v1Json);

            var pm = new ProfileManager(path);

            var x11 = pm.UserData.Profiles["x11"];
            Assert.Equal("root", x11.User);
            Assert.Equal("x11host", x11.HostName);
            Assert.NotNull(x11.ExtraArgs);
            Assert.Contains("-X", x11.ExtraArgs);

            // The generated command must include the preserved flag so behaviour is unchanged.
            Assert.Contains("-X", x11.ToCommandLine());

            var agent = pm.UserData.Profiles["agent"];
            Assert.NotNull(agent.ExtraArgs);
            Assert.Contains("-A", agent.ExtraArgs);
        }

        // ── CustomShell ───────────────────────────────────────────────────────────

        [Fact]
        public void CustomShell_AddAndPersist()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);
            pm.UserData.CustomShell["wt"] = "wt.exe -p";

            var pm2 = new ProfileManager(path);
            Assert.True(pm2.UserData.CustomShell.ContainsKey("wt"));
            Assert.Equal("wt.exe -p", pm2.UserData.CustomShell["wt"]);
        }

        [Fact]
        public void SelectedCustomShell_PersistsAcrossReload()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);
            pm.UserData.CustomShell.SetCallback(null);
            pm.UserData.CustomShell["myshell"] = "myshell.exe";
            pm.UserData.SelectedCustomShell = "myshell";
            pm.UserData.CustomShell.SetCallback(pm.SaveConfiguration);
            pm.SaveConfiguration();

            var pm2 = new ProfileManager(path);
            Assert.Equal("myshell", pm2.UserData.SelectedCustomShell);
        }

        // ── AutoSaveDictionary ────────────────────────────────────────────────────

        [Fact]
        public void AutoSaveDictionary_SetCallbackNull_SuppressesSave()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);

            pm.UserData.Profiles.SetCallback(null);
            pm.UserData.Profiles["silent"] = new SshProfile { HostName = "silent.host" };

            var pm2 = new ProfileManager(path);
            Assert.False(pm2.UserData.Profiles.ContainsKey("silent"),
                "Profile should not be saved while callback is null.");

            pm.UserData.Profiles.SetCallback(pm.SaveConfiguration);
            pm.SaveConfiguration();

            var pm3 = new ProfileManager(path);
            Assert.True(pm3.UserData.Profiles.ContainsKey("silent"));
        }

        [Fact]
        public void AutoSaveDictionary_Clear_TriggersCallback()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);
            pm.UserData.Profiles["a"] = new SshProfile { HostName = "a.host" };
            pm.UserData.Profiles["b"] = new SshProfile { HostName = "b.host" };

            pm.UserData.Profiles.Clear();

            var pm2 = new ProfileManager(path);
            Assert.Empty(pm2.UserData.Profiles);
        }

        [Fact]
        public void AutoSaveDictionary_Count_ReflectsCurrentState()
        {
            var path = GetTmpPath();
            var pm = new ProfileManager(path);
            Assert.Empty(pm.UserData.Profiles);

            pm.UserData.Profiles["x"] = new SshProfile { HostName = "x.host" };
            Assert.Single(pm.UserData.Profiles);

            pm.UserData.Profiles.Remove("x");
            Assert.Empty(pm.UserData.Profiles);
        }
    }
}

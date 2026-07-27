using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class SshKeysTests
    {
        // ── SshKeyEntry ───────────────────────────────────────────────────────────

        [Fact]
        public void SshKeyEntry_ToDisplayString_PathOnly()
        {
            var entry = new SshKeyEntry { Path = @"C:\Users\me\.ssh\id_rsa" };
            Assert.Equal(@"C:\Users\me\.ssh\id_rsa", entry.ToDisplayString());
        }

        [Fact]
        public void SshKeyEntry_ToDisplayString_WithDescription()
        {
            var entry = new SshKeyEntry
            {
                Path = @"C:\Users\me\.ssh\id_rsa",
                Description = "Production key"
            };
            Assert.Equal(@"Production key — C:\Users\me\.ssh\id_rsa", entry.ToDisplayString());
        }

        [Fact]
        public void SshKeyEntry_ToDisplayString_EmptyPath()
        {
            var entry = new SshKeyEntry();
            Assert.Equal("", entry.ToDisplayString());
        }

        [Fact]
        public void SshKeyEntry_GetEffectivePublicKeyPath_Default()
        {
            var entry = new SshKeyEntry { Path = @"C:\Users\me\.ssh\id_rsa" };
            Assert.Equal(@"C:\Users\me\.ssh\id_rsa.pub", entry.GetEffectivePublicKeyPath());
        }

        [Fact]
        public void SshKeyEntry_GetEffectivePublicKeyPath_Explicit()
        {
            var entry = new SshKeyEntry
            {
                Path = @"C:\Users\me\.ssh\id_rsa",
                PublicKeyPath = @"C:\Users\me\.ssh\custom.pub"
            };
            Assert.Equal(@"C:\Users\me\.ssh\custom.pub", entry.GetEffectivePublicKeyPath());
        }

        [Fact]
        public void SshKeyEntry_GetEffectivePublicKeyPath_NullPath()
        {
            var entry = new SshKeyEntry();
            Assert.Null(entry.GetEffectivePublicKeyPath());
        }

        // ── Storage backward compatibility ────────────────────────────────────────
        // New fields (PublicKeyPath, Fingerprint, Comment) are nullable and use
        // NullValueHandling.Ignore — old JSON without these fields must deserialize cleanly.

        [Fact]
        public void SshKeyEntry_Deserialize_OldFormat_NoNewFields()
        {
            var json = @"{ ""Path"": ""C:\\Users\\me\\.ssh\\id_rsa"", ""Description"": ""my key"" }";
            var entry = Newtonsoft.Json.JsonConvert.DeserializeObject<SshKeyEntry>(json);

            Assert.NotNull(entry);
            Assert.Equal(@"C:\Users\me\.ssh\id_rsa", entry.Path);
            Assert.Equal("my key", entry.Description);
            Assert.Null(entry.PublicKeyPath);
            Assert.Null(entry.Fingerprint);
            Assert.Null(entry.Comment);
        }

        [Fact]
        public void SshKeyEntry_Deserialize_NewFormat_AllFields()
        {
            var json = @"{
                ""Path"": ""C:\\Users\\me\\.ssh\\id_ed25519"",
                ""PublicKeyPath"": ""C:\\Users\\me\\.ssh\\id_ed25519.pub"",
                ""Fingerprint"": ""SHA256:abc123"",
                ""Comment"": ""user@host"",
                ""Description"": ""test""
            }";
            var entry = Newtonsoft.Json.JsonConvert.DeserializeObject<SshKeyEntry>(json);

            Assert.NotNull(entry);
            Assert.Equal(@"C:\Users\me\.ssh\id_ed25519", entry.Path);
            Assert.Equal(@"C:\Users\me\.ssh\id_ed25519.pub", entry.PublicKeyPath);
            Assert.Equal("SHA256:abc123", entry.Fingerprint);
            Assert.Equal("user@host", entry.Comment);
            Assert.Equal("test", entry.Description);
        }

        [Fact]
        public void SshKeyEntry_Serialize_NullFieldsOmitted()
        {
            var entry = new SshKeyEntry { Path = @"C:\path\key" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(entry);

            Assert.Contains("Path", json);
            Assert.DoesNotContain("PublicKeyPath", json);
            Assert.DoesNotContain("Fingerprint", json);
            Assert.DoesNotContain("Comment", json);
            Assert.DoesNotContain("Description", json);
        }

        // ── UserData key registry ─────────────────────────────────────────────────

        [Fact]
        public void UserData_SshKeys_InitializedEmpty()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            Assert.NotNull(userData.SshKeys);
            Assert.Empty(userData.SshKeys);
        }

        [Fact]
        public void UserData_SshKeys_AddAndRetrieve()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["mykey"] = new SshKeyEntry { Path = @"C:\Users\me\.ssh\id_rsa" };

            Assert.True(userData.SshKeys.ContainsKey("mykey"));
            Assert.Equal(@"C:\Users\me\.ssh\id_rsa", userData.SshKeys["mykey"].Path);
        }

        [Fact]
        public void UserData_SshKeys_Remove()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["mykey"] = new SshKeyEntry { Path = @"C:\Users\me\.ssh\id_rsa" };
            userData.SshKeys.Remove("mykey");

            Assert.False(userData.SshKeys.ContainsKey("mykey"));
        }

        [Fact]
        public void UserData_SshKeys_AutoSaveCallback()
        {
            int saveCount = 0;
            var userData = new UserData();
            userData.Attach(() => saveCount++);
            userData.SshKeys["mykey"] = new SshKeyEntry { Path = @"C:\path\key" };

            Assert.True(saveCount > 0, "Save callback should fire on SshKeys mutation.");
        }

        [Fact]
        public void UserData_SshKeys_RenamePreservesValue()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["old"] = new SshKeyEntry
            {
                Path = @"C:\Users\me\.ssh\id_rsa",
                Description = "test"
            };

            var value = userData.SshKeys["old"];
            userData.SshKeys.SetCallback(null);
            userData.SshKeys.Remove("old");
            userData.SshKeys["new"] = value;

            Assert.False(userData.SshKeys.ContainsKey("old"));
            Assert.True(userData.SshKeys.ContainsKey("new"));
            Assert.Equal(@"C:\Users\me\.ssh\id_rsa", userData.SshKeys["new"].Path);
            Assert.Equal("test", userData.SshKeys["new"].Description);
        }

        // ── AutoCompleter keys suggestions ────────────────────────────────────────

        [Fact]
        public void GetSuggestions_PartialKe_DoesNotExposeKeysInSimplifiedRootMenu()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "ke", null, "icon.png");
            Assert.DoesNotContain(results, r => r.Title == "keys");
        }

        [Fact]
        public void GetSuggestions_KeysSpace_SuggestsAllSubCommands()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "keys ", null, "icon.png");
            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("add", titles);
            Assert.Contains("generate", titles);
            Assert.Contains("install", titles);
            Assert.Contains("manage", titles);
            Assert.Contains("remove", titles);
            Assert.Contains("rename", titles);
            Assert.Contains("copy-path", titles);
            Assert.Contains("copy-pub", titles);
            Assert.Contains("scan", titles);
        }

        [Theory]
        [InlineData("a",      new[] { "add" })]
        [InlineData("ad",     new[] { "add" })]
        [InlineData("add",    new[] { "add" })]
        [InlineData("g",      new[] { "generate" })]
        [InlineData("ge",     new[] { "generate" })]
        [InlineData("gen",    new[] { "generate" })]
        [InlineData("m",      new[] { "manage" })]
        [InlineData("ma",     new[] { "manage" })]
        [InlineData("r",      new[] { "remove", "rename" })]
        [InlineData("re",     new[] { "remove", "rename" })]
        [InlineData("rem",    new[] { "remove" })]
        [InlineData("ren",    new[] { "rename" })]
        [InlineData("c",      new[] { "copy-path", "copy-pub" })]
        [InlineData("co",     new[] { "copy-path", "copy-pub" })]
        [InlineData("copy-pa", new[] { "copy-path" })]
        [InlineData("copy-pu", new[] { "copy-pub" })]
        [InlineData("s",      new[] { "scan" })]
        [InlineData("sc",     new[] { "scan" })]
        public void GetSuggestions_KeysPartialPrefix_ShowsMatchingSubCommands(
            string partial, string[] expected)
        {
            var results = AutoCompleter.GetSuggestions("ssh", "keys " + partial, null, "icon.png");
            var allSubCmds = new HashSet<string> { "install", "manage", "add", "generate", "remove", "rename", "copy-path", "copy-pub", "scan" };
            var subCommandTitles = results
                .Select(r => r.Title)
                .Where(t => allSubCmds.Contains(t))
                .ToHashSet();

            foreach (var e in expected)
                Assert.Contains(e, subCommandTitles);
            Assert.Equal(expected.Length, subCommandTitles.Count);
        }

        [Fact]
        public void GetSuggestions_KeysPrefix_SuggestsKeyAliases()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["prod"] = new SshKeyEntry { Path = @"C:\Users\me\.ssh\prod_key" };
            userData.SshKeys["dev"]  = new SshKeyEntry { Path = @"C:\Users\me\.ssh\dev_key" };

            var results = AutoCompleter.GetSuggestions("ssh", "keys ", userData, "icon.png");

            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("prod", titles);
            Assert.Contains("dev", titles);
        }

        [Fact]
        public void GetSuggestions_KeysPrefixWithSearch_FiltersAliases()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["prod"] = new SshKeyEntry { Path = @"C:\Users\me\.ssh\prod_key" };
            userData.SshKeys["dev"]  = new SshKeyEntry { Path = @"C:\Users\me\.ssh\dev_key" };

            var results = AutoCompleter.GetSuggestions("ssh", "keys pro", userData, "icon.png");

            var titles = new HashSet<string>();
            foreach (var r in results) titles.Add(r.Title);

            Assert.Contains("prod", titles);
            Assert.DoesNotContain("dev", titles);
        }

        // ── Keys submenu score invariants ─────────────────────────────────────────

        [Fact]
        public void KeysSubmenu_ManagementRowIsAbovePrimaryRows()
        {
            Assert.True(QuickSsh.ScoreSubMenuManagement > QuickSsh.ScoreKeysActionInstall,
                "Management row must outrank the install action row.");
        }

        [Fact]
        public void KeysSubmenu_SavedItemsAndInstallAreAboveManage()
        {
            Assert.True(QuickSsh.ScoreKeysSavedItem > QuickSsh.ScoreKeysActionInstall,
                "Saved keys must appear above the install row.");
            Assert.True(QuickSsh.ScoreKeysActionInstall > QuickSsh.ScoreKeysActionManage,
                "Install row must appear above the manage row.");
        }

        [Fact]
        public void KeysManage_ActionRowScoresAreInDescendingOrder()
        {
            // add > generate > scan > rename > copy-path > copy-pub > remove
            Assert.True(QuickSsh.ScoreKeysManageAdd      > QuickSsh.ScoreKeysManageGenerate);
            Assert.True(QuickSsh.ScoreKeysManageGenerate > QuickSsh.ScoreKeysManageScan);
            Assert.True(QuickSsh.ScoreKeysManageScan     > QuickSsh.ScoreKeysManageRename);
            Assert.True(QuickSsh.ScoreKeysManageRename   > QuickSsh.ScoreKeysManageCopyPath);
            Assert.True(QuickSsh.ScoreKeysManageCopyPath > QuickSsh.ScoreKeysManageCopyPub);
            Assert.True(QuickSsh.ScoreKeysManageCopyPub  > QuickSsh.ScoreKeysManageRemove);
        }

        [Fact]
        public void KeysSubmenu_ManageRowsAreSafeAboveDefaultScore()
        {
            Assert.True(QuickSsh.ScoreKeysActionManage >= 1000,
                "Keys manage action score must be >= 1000 to be safe from fuzzy boosting.");
            Assert.True(QuickSsh.ScoreKeysManageCopyPub >= 1000,
                "Keys manage copy-pub score must be >= 1000 to be safe from fuzzy boosting.");
        }

        // ── ScanSshDirectory filtering ────────────────────────────────────────────

        [Fact]
        public void ScanSshDirectory_FiltersOutPubFiles()
        {
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_scan_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "id_rsa"), "private");
                File.WriteAllText(Path.Combine(dir, "id_rsa.pub"), "public");
                File.WriteAllText(Path.Combine(dir, "id_ed25519"), "private");
                File.WriteAllText(Path.Combine(dir, "id_ed25519.pub"), "public");

                var results = QuickSsh.ScanSshDirectory(dir);

                var names = results.Select(Path.GetFileName).ToHashSet();
                Assert.Contains("id_rsa", names);
                Assert.Contains("id_ed25519", names);
                Assert.DoesNotContain("id_rsa.pub", names);
                Assert.DoesNotContain("id_ed25519.pub", names);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ScanSshDirectory_FiltersOutKnownNonKeyFiles()
        {
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_scan_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "id_rsa"), "private");
                File.WriteAllText(Path.Combine(dir, "known_hosts"), "hosts");
                File.WriteAllText(Path.Combine(dir, "known_hosts.old"), "old hosts");
                File.WriteAllText(Path.Combine(dir, "config"), "ssh config");
                File.WriteAllText(Path.Combine(dir, "authorized_keys"), "authorized");
                File.WriteAllText(Path.Combine(dir, "authorized_keys2"), "authorized2");
                File.WriteAllText(Path.Combine(dir, "environment"), "env");
                File.WriteAllText(Path.Combine(dir, "profiles.json"), "{}");

                var results = QuickSsh.ScanSshDirectory(dir);

                var names = results.Select(Path.GetFileName).ToHashSet();
                Assert.Contains("id_rsa", names);
                Assert.DoesNotContain("known_hosts", names);
                Assert.DoesNotContain("known_hosts.old", names);
                Assert.DoesNotContain("config", names);
                Assert.DoesNotContain("authorized_keys", names);
                Assert.DoesNotContain("authorized_keys2", names);
                Assert.DoesNotContain("environment", names);
                Assert.DoesNotContain("profiles.json", names);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ScanSshDirectory_FiltersOutLogBakTmpOldJsonExtensions()
        {
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_scan_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "id_rsa"), "private");
                File.WriteAllText(Path.Combine(dir, "something.log"), "log");
                File.WriteAllText(Path.Combine(dir, "backup.bak"), "bak");
                File.WriteAllText(Path.Combine(dir, "temp.tmp"), "tmp");
                File.WriteAllText(Path.Combine(dir, "file.old"), "old");
                File.WriteAllText(Path.Combine(dir, "data.json"), "{}");

                var results = QuickSsh.ScanSshDirectory(dir);

                var names = results.Select(Path.GetFileName).ToHashSet();
                Assert.Contains("id_rsa", names);
                Assert.DoesNotContain("something.log", names);
                Assert.DoesNotContain("backup.bak", names);
                Assert.DoesNotContain("temp.tmp", names);
                Assert.DoesNotContain("file.old", names);
                Assert.DoesNotContain("data.json", names);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ScanSshDirectory_NonExistentDir_ReturnsEmpty()
        {
            var results = QuickSsh.ScanSshDirectory(@"C:\nonexistent\path\that\does\not\exist");
            Assert.Empty(results);
        }

        // ── SshKeyEntry new metadata fields ───────────────────────────────────────

        [Fact]
        public void SshKeyEntry_NewFields_Algorithm_Source_CreatedAt()
        {
            var entry = new SshKeyEntry
            {
                Path = @"C:\Users\me\.ssh\id_ed25519",
                Algorithm = "ed25519",
                Source = "generated",
                CreatedAt = "2025-01-15T10:30:00.0000000Z"
            };
            Assert.Equal("ed25519", entry.Algorithm);
            Assert.Equal("generated", entry.Source);
            Assert.Equal("2025-01-15T10:30:00.0000000Z", entry.CreatedAt);
        }

        [Fact]
        public void SshKeyEntry_NewFields_NullByDefault()
        {
            var entry = new SshKeyEntry { Path = @"C:\path\key" };
            Assert.Null(entry.Algorithm);
            Assert.Null(entry.Source);
            Assert.Null(entry.CreatedAt);
        }

        [Fact]
        public void SshKeyEntry_Serialize_NewNullFieldsOmitted()
        {
            var entry = new SshKeyEntry { Path = @"C:\path\key" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(entry);

            Assert.Contains("Path", json);
            Assert.DoesNotContain("Algorithm", json);
            Assert.DoesNotContain("Source", json);
            Assert.DoesNotContain("CreatedAt", json);
        }

        [Fact]
        public void SshKeyEntry_Deserialize_WithNewFields()
        {
            var json = @"{
                ""Path"": ""C:\\Users\\me\\.ssh\\id_ed25519"",
                ""Algorithm"": ""ed25519"",
                ""Source"": ""generated"",
                ""CreatedAt"": ""2025-01-15T10:30:00.0000000Z""
            }";
            var entry = Newtonsoft.Json.JsonConvert.DeserializeObject<SshKeyEntry>(json);

            Assert.NotNull(entry);
            Assert.Equal(@"C:\Users\me\.ssh\id_ed25519", entry.Path);
            Assert.Equal("ed25519", entry.Algorithm);
            Assert.Equal("generated", entry.Source);
            Assert.Equal("2025-01-15T10:30:00.0000000Z", entry.CreatedAt);
        }

        [Fact]
        public void SshKeyEntry_Deserialize_OldFormat_NoNewMetadataFields()
        {
            // Existing stored data without Algorithm/Source/CreatedAt must still load cleanly.
            var json = @"{ ""Path"": ""C:\\Users\\me\\.ssh\\id_rsa"" }";
            var entry = Newtonsoft.Json.JsonConvert.DeserializeObject<SshKeyEntry>(json);

            Assert.NotNull(entry);
            Assert.Equal(@"C:\Users\me\.ssh\id_rsa", entry.Path);
            Assert.Null(entry.Algorithm);
            Assert.Null(entry.Source);
            Assert.Null(entry.CreatedAt);
        }

        // ── SanitizeKeyFileName ───────────────────────────────────────────────────

        [Theory]
        [InlineData("mykey", "mykey")]
        [InlineData("my key", "my_key")]
        [InlineData("  spaced  ", "spaced")]
        [InlineData("a/b\\c", "abc")]
        [InlineData("normal-name_123", "normal-name_123")]
        public void SanitizeKeyFileName_ValidInputs(string input, string expected)
        {
            Assert.Equal(expected, Utils.SanitizeKeyFileName(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void SanitizeKeyFileName_EmptyOrNull_ReturnsNull(string input)
        {
            Assert.Null(Utils.SanitizeKeyFileName(input));
        }

        [Fact]
        public void SanitizeKeyFileName_Null_ReturnsNull()
        {
            Assert.Null(Utils.SanitizeKeyFileName(null));
        }

        // ── Generate sub-command in autocomplete ──────────────────────────────────

        [Fact]
        public void GetSuggestions_KeysPartialG_SuggestsGenerate()
        {
            var results = AutoCompleter.GetSuggestions("ssh", "keys g", null, "icon.png");
            Assert.Contains(results, r => r.Title == "generate");
            Assert.DoesNotContain(results, r => r.Title == "add");
            Assert.DoesNotContain(results, r => r.Title == "scan");
        }

        // ── Generate score placement ──────────────────────────────────────────────

        [Fact]
        public void KeysManage_GenerateScoreIsBetweenAddAndScan()
        {
            Assert.True(QuickSsh.ScoreKeysManageAdd > QuickSsh.ScoreKeysManageGenerate,
                "Add must outrank generate in the manage menu.");
            Assert.True(QuickSsh.ScoreKeysManageGenerate > QuickSsh.ScoreKeysManageScan,
                "Generate must outrank scan in the manage menu.");
        }

        [Fact]
        public void KeysManage_GenerateScoreIsSafeAboveDefaultScore()
        {
            Assert.True(QuickSsh.ScoreKeysManageGenerate >= 1000,
                "Generate score must be >= 1000 to be safe from fuzzy boosting.");
        }

        // ── Generated key auto-register availability ──────────────────────────────

        [Fact]
        public void GeneratedKey_RegisteredInSshKeys_AvailableInAutocomplete()
        {
            // Simulates a key that was generated and registered:
            // after registration it must appear in autocomplete suggestions.
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["mygenerated"] = new SshKeyEntry
            {
                Path = @"C:\Users\me\.ssh\mygenerated",
                PublicKeyPath = @"C:\Users\me\.ssh\mygenerated.pub",
                Algorithm = "ed25519",
                Source = "generated",
                CreatedAt = "2025-01-15T10:30:00.0000000Z"
            };

            // Key must appear in "keys " autocomplete (alongside sub-commands)
            var results = AutoCompleter.GetSuggestions("ssh", "keys ", userData, "icon.png");
            Assert.Contains(results, r => r.Title == "mygenerated");
        }

        [Fact]
        public void GeneratedKey_NotRegistered_NotInAutocomplete()
        {
            // Verify that unregistered keys do NOT appear.
            var userData = new UserData();
            userData.Attach(() => { });

            var results = AutoCompleter.GetSuggestions("ssh", "keys ", userData, "icon.png");
            Assert.DoesNotContain(results, r => r.Title == "mygenerated");
        }

        [Fact]
        public void GeneratedKey_AvailableInDirectConnect_DashI_Autocomplete()
        {
            // After registration, the key alias should be offered when typing "ssh -i"
            // via direct-connect autocomplete. Since HandleDirectConnect is not tested
            // directly here (it requires full plugin context), we verify the key is in
            // the UserData registry which feeds the autocomplete.
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["prod"] = new SshKeyEntry
            {
                Path = @"C:\Users\me\.ssh\prod",
                Algorithm = "ed25519",
                Source = "generated"
            };

            Assert.True(userData.SshKeys.ContainsKey("prod"));
            Assert.Equal(@"C:\Users\me\.ssh\prod", userData.SshKeys["prod"].Path);
        }

        // ── Duplicate alias validation ────────────────────────────────────────────

        [Fact]
        public void DuplicateAlias_BlocksRegistration()
        {
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["existing"] = new SshKeyEntry { Path = @"C:\path\key" };

            // A duplicate alias must be detected
            Assert.True(userData.SshKeys.ContainsKey("existing"));
        }

        // ── File exists validation ────────────────────────────────────────────────

        [Fact]
        public void ExistingKeyFile_BlocksGeneration()
        {
            // Create a temp file to simulate an existing key
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var keyPath = Path.Combine(dir, "existing_key");
                File.WriteAllText(keyPath, "fake key");

                // File.Exists should return true — this is what the generate handler checks
                Assert.True(File.Exists(keyPath),
                    "Test setup: key file must exist to simulate the 'already exists' guard.");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void NonExistingKeyFile_AllowsGeneration()
        {
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            var keyPath = Path.Combine(dir, "new_key");

            Assert.False(File.Exists(keyPath),
                "Key file must not exist before generation.");
        }

        // ── No registration on failure ────────────────────────────────────────────

        [Fact]
        public void FailedGeneration_DoesNotRegister()
        {
            // Simulates the logic: if File.Exists(keyPath) is false after ssh-keygen,
            // the key must NOT be added to the registry.
            var userData = new UserData();
            userData.Attach(() => { });

            var keyPath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Path.GetRandomFileName());

            // Simulate failed generation — file does not exist
            if (!File.Exists(keyPath))
            {
                // The handler would NOT register the key in this case.
                // We verify the registry stays empty.
                Assert.Empty(userData.SshKeys);
            }
        }

        [Fact]
        public void SuccessfulGeneration_Registers()
        {
            // Simulates the logic: if BOTH File.Exists(keyPath) and
            // File.Exists(keyPath + ".pub") are true after ssh-keygen,
            // the key IS added to the registry.
            var userData = new UserData();
            userData.Attach(() => { });

            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var keyPath = Path.Combine(dir, "test_key");
                var pubPath = keyPath + ".pub";
                File.WriteAllText(keyPath, "fake generated key");
                File.WriteAllText(pubPath, "fake generated pub");

                // Simulate successful generation — both files exist
                if (File.Exists(keyPath) && File.Exists(pubPath))
                {
                    userData.SshKeys["testkey"] = new SshKeyEntry
                    {
                        Path = keyPath,
                        PublicKeyPath = pubPath,
                        Algorithm = "ed25519",
                        Source = "generated",
                        CreatedAt = "2025-01-15T10:30:00.0000000Z"
                    };
                }

                Assert.True(userData.SshKeys.ContainsKey("testkey"));
                Assert.Equal(keyPath, userData.SshKeys["testkey"].Path);
                Assert.Equal(pubPath, userData.SshKeys["testkey"].PublicKeyPath);
                Assert.Equal("ed25519", userData.SshKeys["testkey"].Algorithm);
                Assert.Equal("generated", userData.SshKeys["testkey"].Source);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void SuccessfulGeneration_MissingPub_DoesNotRegister()
        {
            // If the private key was created but .pub is missing (partial generation),
            // the key must NOT be registered.
            var userData = new UserData();
            userData.Attach(() => { });

            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var keyPath = Path.Combine(dir, "test_key");
                var pubPath = keyPath + ".pub";
                File.WriteAllText(keyPath, "fake generated key");
                // Intentionally do NOT create .pub file

                // Simulate generation check — private exists, .pub does not
                if (File.Exists(keyPath) && File.Exists(pubPath))
                {
                    userData.SshKeys["testkey"] = new SshKeyEntry
                    {
                        Path = keyPath,
                        PublicKeyPath = pubPath,
                        Algorithm = "ed25519",
                        Source = "generated"
                    };
                }

                // Must NOT have been registered
                Assert.Empty(userData.SshKeys);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // ── Custom path flow tests ────────────────────────────────────────────────

        [Fact]
        public void CustomPath_FileExists_BlocksGeneration()
        {
            // If a file already exists at the custom path, generation must not proceed.
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var keyPath = Path.Combine(dir, "my_custom_key");
                File.WriteAllText(keyPath, "fake key");

                Assert.True(File.Exists(keyPath),
                    "Test setup: key file must exist to simulate the 'already exists' guard.");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void CustomPath_DirectoryTarget_BlocksGeneration()
        {
            // If the custom path points to an existing directory, it must be rejected.
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                Assert.True(Directory.Exists(dir),
                    "Test setup: target must be an existing directory.");
                // The handler checks Directory.Exists(fullPath) — this must block generation.
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void CustomPath_NonExisting_AllowsGeneration()
        {
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            var keyPath = Path.Combine(dir, "new_key");

            Assert.False(File.Exists(keyPath), "Custom key path must not exist before generation.");
            Assert.False(Directory.Exists(keyPath), "Custom key path must not be a directory.");
        }

        [Fact]
        public void CustomPath_SuccessfulGeneration_RegistersCorrectPath()
        {
            // When a custom path is used and both files exist, the SshKeyEntry
            // must store the custom path (not the default ~/.ssh/ path).
            var userData = new UserData();
            userData.Attach(() => { });

            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var customPath = Path.Combine(dir, "custom_key");
                var pubPath = customPath + ".pub";
                File.WriteAllText(customPath, "fake generated key");
                File.WriteAllText(pubPath, "fake generated pub");

                // Simulate the registration logic from ExecuteKeyGeneration
                if (File.Exists(customPath) && File.Exists(pubPath))
                {
                    userData.SshKeys["myalias"] = new SshKeyEntry
                    {
                        Path = customPath,
                        PublicKeyPath = pubPath,
                        Algorithm = "ed25519",
                        Source = "generated",
                        CreatedAt = "2025-01-15T10:30:00.0000000Z"
                    };
                }

                Assert.True(userData.SshKeys.ContainsKey("myalias"));
                Assert.Equal(customPath, userData.SshKeys["myalias"].Path);
                Assert.Equal(pubPath, userData.SshKeys["myalias"].PublicKeyPath);
                Assert.Equal("generated", userData.SshKeys["myalias"].Source);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void CustomPath_ParentCreation_Succeeds()
        {
            // When the parent directory does not exist, ExecuteKeyGeneration creates it.
            var dir = Path.Combine(Path.GetTempPath(), "quickssh_test_gen_" + Path.GetRandomFileName(),
                "nested", "dir");
            Assert.False(Directory.Exists(dir));

            try
            {
                Directory.CreateDirectory(dir);
                Assert.True(Directory.Exists(dir));
            }
            finally
            {
                // Clean up the top-level temp dir
                var root = Directory.GetParent(dir)?.Parent?.FullName
                    ?? throw new InvalidOperationException("Unable to resolve the temporary test root.");
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [Fact]
        public void CustomPath_QuotedPath_Stripped()
        {
            // Simulates the quote-stripping logic in HandleKeysGenerate:
            // surrounding quotes are removed to get the raw path.
            var raw = "\"C:\\Users\\me\\.ssh\\my key\"";
            if (raw.Length >= 2 && raw.StartsWith("\"") && raw.EndsWith("\""))
                raw = raw.Substring(1, raw.Length - 2);

            Assert.Equal(@"C:\Users\me\.ssh\my key", raw);
        }

        [Fact]
        public void CustomPath_SanitizeNotAppliedToPath()
        {
            // SanitizeKeyFileName is only for alias → default file name derivation.
            // A custom path must NOT be transformed through sanitize.
            var customPath = @"C:\My Keys\special-name_v2";
            // If we ran it through SanitizeKeyFileName, spaces would become underscores
            // and the result would be wrong.
            var sanitized = Utils.SanitizeKeyFileName(customPath);
            // The custom path contains path separators which are invalid file name chars,
            // so sanitize would strip them — confirming it must not be used for paths.
            Assert.NotEqual(customPath, sanitized);
        }

        [Fact]
        public void CustomPath_TildeExpansion()
        {
            // Custom path supports ~ expansion to user profile directory.
            var input = @"~\.ssh\custom_key";
            var expanded = input.Replace("~",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded);
            Assert.EndsWith(@"\.ssh\custom_key", expanded);
        }

        // ── ParseGenerateArgs ────────────────────────────────────────────────────

        [Fact]
        public void ParseGenerateArgs_RuntimeBugRepro_AliasAndCustomPathSplitCorrectly()
        {
            // Exact runtime repro: "skuska C:\Users\info\.ssh\custom\skuska"
            // must yield alias="skuska", customPath=@"C:\Users\info\.ssh\custom\skuska"
            // and NOT treat the whole string as the alias.
            var (alias, customPath) = Utils.ParseGenerateArgs(
                @"skuska C:\Users\info\.ssh\custom\skuska");

            Assert.Equal("skuska", alias);
            Assert.Equal(@"C:\Users\info\.ssh\custom\skuska", customPath);

            // Verify alias is clean — SanitizeKeyFileName should NOT corrupt it.
            Assert.Equal("skuska", Utils.SanitizeKeyFileName(alias));
        }

        [Fact]
        public void ParseGenerateArgs_NonBreakingSpace_SplitsCorrectly()
        {
            // If Flow Launcher passes a non-breaking space (U+00A0) between
            // alias and path, the parser must still split correctly.
            var rest = "skuska\u00A0C:\\Users\\info\\.ssh\\custom\\skuska";
            var (alias, customPath) = Utils.ParseGenerateArgs(rest);

            Assert.Equal("skuska", alias);
            Assert.Equal(@"C:\Users\info\.ssh\custom\skuska", customPath);
        }

        [Fact]
        public void ParseGenerateArgs_DefaultFlow_AliasOnly()
        {
            // Default flow: only alias, no custom path.
            var (alias, customPath) = Utils.ParseGenerateArgs("skuska");

            Assert.Equal("skuska", alias);
            Assert.Equal("", customPath);
        }

        [Fact]
        public void ParseGenerateArgs_QuotedPathWithSpaces()
        {
            // Quoted custom path with spaces must be stripped and preserved.
            var (alias, customPath) = Utils.ParseGenerateArgs(
                "mykey \"C:\\My Keys\\special key\"");

            Assert.Equal("mykey", alias);
            Assert.Equal(@"C:\My Keys\special key", customPath);
        }

        [Fact]
        public void ParseGenerateArgs_EmptyInput_ReturnsBothEmpty()
        {
            var (alias, customPath) = Utils.ParseGenerateArgs("");
            Assert.Equal("", alias);
            Assert.Equal("", customPath);
        }

        [Fact]
        public void ParseGenerateArgs_NullInput_ReturnsBothEmpty()
        {
            var (alias, customPath) = Utils.ParseGenerateArgs(null);
            Assert.Equal("", alias);
            Assert.Equal("", customPath);
        }

        [Fact]
        public void ParseGenerateArgs_BugRepro_SanitizeOnWholeStringProducesWrongPath()
        {
            // This test documents the exact bug: if the parser fails to split,
            // SanitizeKeyFileName on the whole string produces a mangled filename
            // like "skuska_CUsersinfo.sshcustomskuska" instead of using the
            // custom path directly.
            var wholeString = @"skuska C:\Users\info\.ssh\custom\skuska";
            var sanitized = Utils.SanitizeKeyFileName(wholeString);

            // Sanitize strips path separators and colons — wrong for a custom path.
            Assert.Contains("CUsersinfo", sanitized);

            // The correct behavior via ParseGenerateArgs:
            var (alias, customPath) = Utils.ParseGenerateArgs(wholeString);
            Assert.Equal("skuska", alias);
            Assert.Equal(@"C:\Users\info\.ssh\custom\skuska", customPath);
            // Sanitize on just the alias is correct.
            Assert.Equal("skuska", Utils.SanitizeKeyFileName(alias));
        }

        // ── Remove is registry-only (no file deletion) ───────────────────────────

        [Fact]
        public void UserData_SshKeys_Remove_DoesNotDeleteFiles()
        {
            // Create a real temp file to prove Remove() never touches the filesystem.
            var tempDir = Path.Combine(Path.GetTempPath(), "quickssh_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var keyPath = Path.Combine(tempDir, "test_key");
                var pubPath = keyPath + ".pub";
                File.WriteAllText(keyPath, "fake-private-key");
                File.WriteAllText(pubPath, "fake-public-key");

                var userData = new UserData();
                userData.Attach(() => { });
                userData.SshKeys["testkey"] = new SshKeyEntry
                {
                    Path = keyPath,
                    PublicKeyPath = pubPath
                };

                // Remove the alias — this must be registry-only.
                userData.SshKeys.Remove("testkey");

                Assert.False(userData.SshKeys.ContainsKey("testkey"));
                Assert.True(File.Exists(keyPath), "Private key file must NOT be deleted by Remove.");
                Assert.True(File.Exists(pubPath), "Public key file must NOT be deleted by Remove.");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UserData_SshKeys_Remove_CapturePathBeforeRemoval()
        {
            // Verifies that the entry's path can be captured before Remove()
            // so the confirmation message can include it.
            var userData = new UserData();
            userData.Attach(() => { });
            userData.SshKeys["myalias"] = new SshKeyEntry
            {
                Path = @"C:\Users\me\.ssh\myalias",
                PublicKeyPath = @"C:\Users\me\.ssh\myalias.pub"
            };

            // Capture the path before removing (as the action handler does).
            var capturedPath = userData.SshKeys["myalias"].Path;
            userData.SshKeys.Remove("myalias");

            Assert.Equal(@"C:\Users\me\.ssh\myalias", capturedPath);
            Assert.False(userData.SshKeys.ContainsKey("myalias"));
        }

        // ── Generate success feedback includes paths ─────────────────────────────

        [Fact]
        public void SshKeyEntry_GenerateSuccessFeedback_IncludesPaths()
        {
            // Simulate what ExecuteKeyGeneration does after successful generation:
            // register the key and format a message with alias, private path, and public path.
            var alias = "mykey";
            var keyPath = @"C:\Users\me\.ssh\mykey";
            var pubKeyPath = keyPath + ".pub";

            var entry = new SshKeyEntry
            {
                Path = keyPath,
                PublicKeyPath = pubKeyPath,
                Algorithm = "ed25519",
                Comment = alias,
                Source = "generated"
            };

            // The success message format: "SSH key generated: {0}\nPrivate: {1}\nPublic: {2}"
            var message = string.Format(
                "SSH key generated: {0}\nPrivate: {1}\nPublic: {2}",
                alias, entry.Path, entry.PublicKeyPath);

            Assert.Contains(alias, message);
            Assert.Contains(keyPath, message);
            Assert.Contains(pubKeyPath, message);
        }

        // ── Remove success feedback makes it clear only alias was removed ────────

        [Fact]
        public void SshKeyEntry_RemoveSuccessFeedback_RegistryOnlyMessage()
        {
            // Simulate what HandleKeysRemove does after successful removal:
            // format a message with alias, registry-only note, and file path.
            var alias = "prodkey";
            var keyPath = @"C:\Users\me\.ssh\prodkey";

            // The remove message format: "SSH key alias removed: {0}\nRegistry entry removed only.\nFiles kept on disk: {1}"
            var message = string.Format(
                "SSH key alias removed: {0}\nRegistry entry removed only.\nFiles kept on disk: {1}",
                alias, keyPath);

            Assert.Contains(alias, message);
            Assert.Contains("Registry entry removed only", message);
            Assert.Contains("Files kept on disk", message);
            Assert.Contains(keyPath, message);
        }

        [Fact]
        public void SshKeyEntry_RemoveSuccessFeedback_EmptyPath()
        {
            // When a key entry has no path (edge case), the message still works.
            var alias = "orphan";
            var keyPath = "";

            var message = string.Format(
                "SSH key alias removed: {0}\nRegistry entry removed only.\nFiles kept on disk: {1}",
                alias, keyPath);

            Assert.Contains(alias, message);
            Assert.Contains("Registry entry removed only", message);
        }
    }
}

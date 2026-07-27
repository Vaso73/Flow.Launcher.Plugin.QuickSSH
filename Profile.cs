using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Persisted plugin data: SSH/SCP profiles, reusable actions, custom shells, SSH keys, and selected shell.
    /// </summary>
    public class UserData
    {
        /// <summary>Gets or sets the persisted user-data schema version.</summary>
        public string PluginVersion { get; set; } = "2.0";

        // ── Structured profiles (canonical format, v2+) ────────────────────────────

        [JsonProperty]
        private Dictionary<string, SshProfile> ProfilesLists { get; set; } = new();

        /// <summary>Gets the auto-saving SSH and SCP profile registry.</summary>
        [JsonIgnore]
        public AutoSaveDictionary<string, SshProfile> Profiles { get; private set; }

        // ── Legacy raw-string profiles (v1, migration source only) ─────────────────
        // Kept as nullable so that newly created files never write this field.

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        private Dictionary<string, string> EntriesLists { get; set; }

        [JsonProperty]
        private Dictionary<string, string> CustomShellLists { get; set; } = new();

        /// <summary>Gets the auto-saving custom shell registry.</summary>
        [JsonIgnore]
        public AutoSaveDictionary<string, string> CustomShell { get; private set; }

        /// <summary>Gets or sets the alias of the selected custom shell.</summary>
        public string? SelectedCustomShell { get; set; }

        // ── Reusable SSH action profiles ───────────────────────────────────────

        [JsonProperty]
        private Dictionary<string, CommandProfile> CommandProfilesLists { get; set; } = new();

        /// <summary>Gets the auto-saving reusable SSH action registry.</summary>
        [JsonIgnore]
        public AutoSaveDictionary<string, CommandProfile> CommandProfiles { get; private set; }

        // ── SSH key registry (alias → local path, never stores key content) ───────

        [JsonProperty]
        private Dictionary<string, SshKeyEntry> SshKeysLists { get; set; } = new();

        /// <summary>Gets the auto-saving SSH key metadata registry.</summary>
        [JsonIgnore]
        public AutoSaveDictionary<string, SshKeyEntry> SshKeys { get; private set; }

        /// <summary>
        /// Binds auto-save callbacks after construction or deserialization.
        /// Migrates any legacy raw-string profiles to structured <see cref="SshProfile"/> objects.
        /// </summary>
        /// <param name="onChanged">Callback invoked on every profile, action, shell, or key mutation.</param>
        /// <returns>
        /// <see langword="true"/> when v1 legacy data was found and migrated;
        /// the caller should persist immediately so the disk file reflects the new v2 format.
        /// </returns>
        public bool Attach(Action onChanged)
        {
            ProfilesLists ??= new Dictionary<string, SshProfile>();
            CustomShellLists ??= new Dictionary<string, string>();
            CommandProfilesLists ??= new Dictionary<string, CommandProfile>();
            SshKeysLists ??= new Dictionary<string, SshKeyEntry>();

            bool migrated = false;

            // One-time migration from v1 raw-string storage (EntriesLists) to the canonical
            // structured model (ProfilesLists).  We only migrate entries that are not already
            // present in ProfilesLists so a mixed v1/v2 file is handled safely.
            // Fields that cannot be parsed from the raw command string are preserved verbatim
            // in SshProfile.ExtraArgs so no data is ever silently lost.
            if (EntriesLists != null && EntriesLists.Count > 0)
            {
                foreach (var kvp in EntriesLists)
                {
                    if (!ProfilesLists.ContainsKey(kvp.Key))
                        ProfilesLists[kvp.Key] = SshProfile.ParseFromLegacyCommand(kvp.Value);
                }

                // Null out the legacy field so it is absent from the next serialization.
                EntriesLists = null;
                migrated = true;
            }

            Profiles = new AutoSaveDictionary<string, SshProfile>(ProfilesLists, onChanged);
            CustomShell = new AutoSaveDictionary<string, string>(CustomShellLists, onChanged);
            CommandProfiles = new AutoSaveDictionary<string, CommandProfile>(CommandProfilesLists, onChanged);
            SshKeys = new AutoSaveDictionary<string, SshKeyEntry>(SshKeysLists, onChanged);

            return migrated;
        }
    }

    /// <summary>
    /// Dictionary wrapper that invokes a callback on every mutation.
    /// </summary>
    public sealed class AutoSaveDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private static readonly Action Noop = () => { };
        private readonly IDictionary<TKey, TValue> _inner;
        private Action _onChanged;

        /// <summary>Initializes an auto-saving dictionary wrapper.</summary>
        /// <param name="inner">Dictionary that stores the values.</param>
        /// <param name="onChanged">Callback invoked after every successful mutation.</param>
        public AutoSaveDictionary(IDictionary<TKey, TValue> inner, Action? onChanged)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _onChanged = onChanged ?? Noop;
        }

        /// <summary>Replaces the callback invoked after dictionary mutations.</summary>
        /// <param name="onChanged">New callback, or a no-op callback when null.</param>
        public void SetCallback(Action? onChanged) => _onChanged = onChanged ?? Noop;

        /// <inheritdoc />
        public TValue this[TKey key]
        {
            get => _inner[key];
            set { _inner[key] = value; _onChanged(); }
        }

        /// <inheritdoc />
        public ICollection<TKey> Keys => _inner.Keys;
        /// <inheritdoc />
        public ICollection<TValue> Values => _inner.Values;
        /// <inheritdoc />
        public int Count => _inner.Count;
        /// <inheritdoc />
        public bool IsReadOnly => _inner.IsReadOnly;

        /// <inheritdoc />
        public void Add(TKey key, TValue value) { _inner.Add(key, value); _onChanged(); }
        /// <inheritdoc />
        public void Add(KeyValuePair<TKey, TValue> item) { _inner.Add(item); _onChanged(); }
        /// <inheritdoc />
        public bool Remove(TKey key) { var r = _inner.Remove(key); if (r) _onChanged(); return r; }
        /// <inheritdoc />
        public bool Remove(KeyValuePair<TKey, TValue> item) { var r = _inner.Remove(item); if (r) _onChanged(); return r; }
        /// <inheritdoc />
        public void Clear() { _inner.Clear(); _onChanged(); }
        /// <inheritdoc />
        public bool ContainsKey(TKey key) => _inner.ContainsKey(key);
        /// <inheritdoc />
        public bool Contains(KeyValuePair<TKey, TValue> item) => _inner.Contains(item);
        /// <inheritdoc />
        public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value);
        /// <inheritdoc />
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);
        /// <inheritdoc />
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_inner).GetEnumerator();
    }

    /// <summary>
    /// Manages loading and saving of user data to a JSON file.
    /// </summary>
    public class ProfileManager
    {
        private readonly string _path;

        /// <summary>Gets the currently loaded plugin data.</summary>
        public UserData UserData { get; private set; }

        /// <summary>Gets the fixed pre-import backup path next to the portable database.</summary>
        internal string ImportBackupPath => _path + ".import.bak";

        /// <summary>Initializes a profile manager for the specified JSON storage path.</summary>
        /// <param name="path">Path to the portable profiles database.</param>
        public ProfileManager(string path)
        {
            _path = path;

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_path))
            {
                UserData = new UserData();
                UserData.Attach(SaveConfiguration);
                SaveConfiguration();
            }
            else
            {
                LoadConfiguration();
            }
        }

        /// <summary>Atomically saves the current plugin data to disk.</summary>
        public void SaveConfiguration()
        {
            var json = JsonConvert.SerializeObject(UserData, Formatting.Indented);
            var tmp = _path + ".tmp";
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp))
                    try { File.Delete(tmp); } catch { /* best effort cleanup */ }
            }
        }

        /// <summary>Creates or replaces the portable pre-import backup atomically.</summary>
        internal string CreateImportBackup()
        {
            var backupPath = ImportBackupPath;
            var tmp = backupPath + ".tmp";

            try
            {
                File.Copy(_path, tmp, overwrite: true);
                File.Move(tmp, backupPath, overwrite: true);
                return backupPath;
            }
            finally
            {
                if (File.Exists(tmp))
                    try { File.Delete(tmp); } catch { /* best effort cleanup */ }
            }
        }

        /// <summary>Restores the portable database from a pre-import backup and reloads memory state.</summary>
        internal void RestoreImportBackup(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                throw new FileNotFoundException("Profile import backup was not found.", backupPath);

            var tmp = _path + ".restore.tmp";
            try
            {
                File.Copy(backupPath, tmp, overwrite: true);
                File.Move(tmp, _path, overwrite: true);
                LoadConfiguration();
            }
            finally
            {
                if (File.Exists(tmp))
                    try { File.Delete(tmp); } catch { /* best effort cleanup */ }
            }
        }

        /// <summary>Loads plugin data from disk and performs any required legacy migration.</summary>
        public void LoadConfiguration()
        {
            var json = File.ReadAllText(_path);
            UserData = JsonConvert.DeserializeObject<UserData>(json) ?? new UserData();

            // Attach returns true when v1 raw-string data was migrated.  Persist immediately so
            // the on-disk file switches to the canonical v2 format after the first load.
            bool migrated = UserData.Attach(SaveConfiguration);
            if (migrated)
                SaveConfiguration();
        }
    }
}
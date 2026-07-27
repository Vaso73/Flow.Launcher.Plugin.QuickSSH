using Newtonsoft.Json;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Represents a registered SSH key alias pointing to a local key file path.
    /// Only the alias, paths, and metadata are stored — never the private key content.
    /// </summary>
    public class SshKeyEntry
    {
        /// <summary>Path to the private key file on disk.</summary>
        [JsonProperty]
        public string? Path { get; set; }

        /// <summary>
        /// Path to the corresponding public key file (e.g. id_ed25519.pub).
        /// When null, derived as <see cref="Path"/> + ".pub".
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? PublicKeyPath { get; set; }

        /// <summary>
        /// SSH key fingerprint (e.g. SHA256:...).  Populated by scan or user.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Fingerprint { get; set; }

        /// <summary>
        /// Comment field from the key (e.g. user@host).  Populated by scan or user.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Comment { get; set; }

        /// <summary>Optional human-readable description.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Description { get; set; }

        /// <summary>
        /// Key algorithm (e.g. "ed25519", "rsa").  Populated by generate or scan.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Algorithm { get; set; }

        /// <summary>
        /// How this key was registered (e.g. "generated", "manual", "scanned").
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Source { get; set; }

        /// <summary>
        /// ISO 8601 UTC timestamp of when this entry was created.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? CreatedAt { get; set; }

        /// <summary>Returns a display-friendly summary of this key entry.</summary>
        public string ToDisplayString()
        {
            if (!string.IsNullOrEmpty(Description))
                return Description + " — " + Path;
            return Path ?? "";
        }

        /// <summary>
        /// Returns the effective public key path: explicit <see cref="PublicKeyPath"/>
        /// if set, otherwise <see cref="Path"/> + ".pub".
        /// </summary>
        public string? GetEffectivePublicKeyPath()
        {
            if (!string.IsNullOrEmpty(PublicKeyPath))
                return PublicKeyPath;
            if (!string.IsNullOrEmpty(Path))
                return Path + ".pub";
            return null;
        }
    }
}

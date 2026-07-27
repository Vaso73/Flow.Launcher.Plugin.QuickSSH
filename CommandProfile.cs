using System;
using Newtonsoft.Json;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Persisted, reusable remote action definition.
    /// Actions are executed through a separately selected SSH connection profile.
    /// </summary>
    public class CommandProfile
    {
        /// <summary>Canonical kind identifier for reusable remote-command actions.</summary>
        public const string RemoteCommandKind = "remote-command";

        /// <summary>Action kind. Unknown kinds are retained but never treated as supported.</summary>
        [JsonProperty]
        public string Kind { get; set; } = RemoteCommandKind;

        /// <summary>Single-line remote command text. Must not contain private key material.</summary>
        [JsonProperty]
        public string Command { get; set; }

        /// <summary>Optional human-readable description.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>Optional future TTY preference: force, yes, or no.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string RequestTTY { get; set; }

        /// <summary>Gets whether this action kind is supported by the current plugin version.</summary>
        [JsonIgnore]
        public bool IsSupportedKind =>
            string.Equals(Kind, RemoteCommandKind, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Rejects multiline/null-byte input and recognizable private-key payload markers.
        /// This is a storage guard, not a general shell-safety validator.
        /// </summary>
        public static bool IsSafeToStore(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            if (command.IndexOf('\r') >= 0 || command.IndexOf('\n') >= 0 || command.IndexOf('\0') >= 0)
                return false;

            return command.IndexOf("PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase) < 0 &&
                   command.IndexOf("BEGIN OPENSSH PRIVATE KEY", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>Returns the human-readable action description used in Flow Launcher results.</summary>
        public string ToDisplayString()
        {
            if (!string.IsNullOrWhiteSpace(Description))
                return Description + " — " + (Command ?? "");
            return Command ?? "";
        }
    }
}

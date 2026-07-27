using System;
using System.Collections.Generic;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Normalizes menu-assisted query input and validates names used for saved profiles and actions.
    /// </summary>
    public static class CommandInputGuard
    {
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(
            new[]
            {
                "ssh", "profiles", "actions", "keys", "shell", "config", "help",
                "add", "run", "use", "manage", "remove", "rename", "copy", "export", "import",
                "install", "generate", "scan", "copy-path", "copy-pub"
            },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Removes a command prefix that the user pasted again after selecting a menu item.
        /// Repeats until nested duplicates are gone.
        /// </summary>
        public static string NormalizeNestedCommandInput(
            string input,
            string actionKeyword,
            string commandPath)
        {
            var value = (input ?? string.Empty).Trim();
            var canonical = (commandPath ?? string.Empty).Trim();
            var keyword = (actionKeyword ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(canonical))
                return value;

            var prefixes = string.IsNullOrEmpty(keyword)
                ? new[] { canonical }
                : new[] { keyword + " " + canonical, canonical };

            bool changed;
            do
            {
                changed = false;
                foreach (var prefix in prefixes)
                {
                    if (value.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                        return string.Empty;

                    var prefixWithSpace = prefix + " ";
                    if (value.StartsWith(prefixWithSpace, StringComparison.OrdinalIgnoreCase))
                    {
                        value = value.Substring(prefixWithSpace.Length).TrimStart();
                        changed = true;
                        break;
                    }
                }
            }
            while (changed && !string.IsNullOrEmpty(value));

            return value;
        }

        /// <summary>
        /// Returns true for concise names that are safe to use inside QuickSSH query navigation.
        /// Unicode letters and digits are supported; separators are limited to dot, dash, and underscore.
        /// </summary>
        public static bool IsValidSavedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
                return false;

            if (!char.IsLetterOrDigit(name[0]))
                return false;

            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                    continue;
                return false;
            }

            return true;
        }

        /// <summary>Returns true when a name collides with a QuickSSH command or sub-command.</summary>
        public static bool IsReservedSavedName(string name) =>
            !string.IsNullOrWhiteSpace(name) && ReservedNames.Contains(name.Trim());

        /// <summary>Finds the stored spelling of a key using case-insensitive matching.</summary>
        public static string FindExistingName<T>(
            IEnumerable<KeyValuePair<string, T>> entries,
            string candidate)
        {
            if (entries == null || string.IsNullOrWhiteSpace(candidate))
                return null;

            foreach (var entry in entries)
            {
                if (string.Equals(entry.Key, candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                    return entry.Key;
            }

            return null;
        }
    }
}

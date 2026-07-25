using System;
using System.IO;

namespace Flow.Launcher.Plugin.QuickSSH
{
    internal static class ProfileStorage
    {
        internal const string ProfilesFileName = "profiles.json";

        internal static string GetDefaultLegacyProfilesPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ssh",
                ProfilesFileName);
        }

        internal static string PrepareProfilesPath(string pluginSettingsDirectoryPath)
        {
            return PrepareProfilesPath(
                pluginSettingsDirectoryPath,
                GetDefaultLegacyProfilesPath());
        }

        internal static string PrepareProfilesPath(
            string pluginSettingsDirectoryPath,
            string legacyProfilesPath)
        {
            if (string.IsNullOrWhiteSpace(pluginSettingsDirectoryPath))
                throw new ArgumentException("Plugin settings directory path is required.", nameof(pluginSettingsDirectoryPath));

            var profilesPath = Path.Combine(pluginSettingsDirectoryPath, ProfilesFileName);
            CopyLegacyProfilesIfNeeded(legacyProfilesPath, profilesPath);
            return profilesPath;
        }

        private static void CopyLegacyProfilesIfNeeded(string legacyProfilesPath, string profilesPath)
        {
            if (string.IsNullOrWhiteSpace(legacyProfilesPath))
                return;

            if (PathsEqual(legacyProfilesPath, profilesPath))
                return;

            if (File.Exists(profilesPath) || !File.Exists(legacyProfilesPath))
                return;

            var profilesDir = Path.GetDirectoryName(profilesPath);
            if (!string.IsNullOrEmpty(profilesDir) && !Directory.Exists(profilesDir))
                Directory.CreateDirectory(profilesDir);

            var tmp = profilesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(legacyProfilesPath, tmp);
                try
                {
                    File.Move(tmp, profilesPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(profilesPath))
                {
                    // Another startup path created the target first; keep that file.
                }
            }
            finally
            {
                if (File.Exists(tmp))
                    try { File.Delete(tmp); } catch { /* best effort cleanup */ }
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                left = Path.GetFullPath(left);
                right = Path.GetFullPath(right);
            }
            catch
            {
                // Fall back to the original strings when a path cannot be normalised.
            }

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}

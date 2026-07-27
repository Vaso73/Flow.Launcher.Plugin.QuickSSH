using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>Result of a guarded profile import.</summary>
    internal sealed class ProfileImportResult
    {
        internal ProfileImportResult(int importedCount, int skippedCount, string backupPath)
        {
            ImportedCount = importedCount;
            SkippedCount = skippedCount;
            BackupPath = backupPath;
        }

        internal int ImportedCount { get; }
        internal int SkippedCount { get; }
        internal string BackupPath { get; }
    }

    /// <summary>
    /// Imports profiles with a portable pre-import backup and fail-closed rollback.
    /// </summary>
    internal static class ProfileImportService
    {
        internal static ProfileImportResult Import(
            ProfileManager profileManager,
            IReadOnlyDictionary<string, SshProfile> importedProfiles,
            Action? saveConfiguration = null)
        {
            if (profileManager == null)
                throw new ArgumentNullException(nameof(profileManager));
            if (importedProfiles == null)
                throw new ArgumentNullException(nameof(importedProfiles));

            var backupPath = profileManager.CreateImportBackup();
            var profiles = profileManager.UserData.Profiles;
            var importedCount = 0;
            var skippedCount = 0;
            var stateReloaded = false;

            profiles.SetCallback(null);
            try
            {
                foreach (var entry in importedProfiles)
                {
                    var nameExists = profiles.Keys.Any(existing =>
                        string.Equals(existing, entry.Key, StringComparison.OrdinalIgnoreCase));

                    if (nameExists)
                    {
                        skippedCount++;
                        continue;
                    }

                    profiles[entry.Key] = entry.Value;
                    importedCount++;
                }

                if (importedCount > 0)
                    (saveConfiguration ?? profileManager.SaveConfiguration)();

                return new ProfileImportResult(importedCount, skippedCount, backupPath);
            }
            catch (Exception importException)
            {
                try
                {
                    profileManager.RestoreImportBackup(backupPath);
                    stateReloaded = true;
                }
                catch (Exception rollbackException)
                {
                    throw new IOException(
                        "Profile import failed and the previous portable database could not be restored.",
                        new AggregateException(importException, rollbackException));
                }

                throw;
            }
            finally
            {
                if (!stateReloaded)
                    profiles.SetCallback(profileManager.SaveConfiguration);
            }
        }
    }
}

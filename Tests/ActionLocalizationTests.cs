using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class ActionLocalizationTests
    {
        [Fact]
        public void AllLanguages_ContainContextualActionRunKeys()
        {
            var languagesDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Languages"));
            if (!Directory.Exists(languagesDir))
                return;

            var requiredKeys = new[]
            {
                "plugin_quickssh_actions_select_profile_subtitle",
                "plugin_quickssh_actions_select_action_subtitle",
                "plugin_quickssh_actions_confirm_subtitle",
                "plugin_quickssh_actions_command_label",
                "plugin_quickssh_actions_execute_subtitle",
                "plugin_quickssh_title_commandactions_manage",
                "plugin_quickssh_subtitle_commandactions_manage",
                "plugin_quickssh_actions_select_profile_for_action",
                "plugin_quickssh_actions_confirm_title",
                "plugin_quickssh_actions_execute_named_title",
                "plugin_quickssh_actions_execute_named_subtitle",
                "plugin_quickssh_title_commandprofiles_manage",
                "plugin_quickssh_subtitle_commandprofiles_manage"
            };

            var files = Directory.GetFiles(languagesDir, "*.xaml");
            Assert.Equal(7, files.Length);

            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var file in files)
            {
                var document = XDocument.Load(file);
                var keys = document.Descendants()
                    .Select(element => (string?)element.Attribute(x + "Key"))
                    .OfType<string>()
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var requiredKey in requiredKeys)
                    Assert.Contains(requiredKey, keys);
            }
        }

        [Fact]
        public void AllLanguages_ContainKeysInstallKeys()
        {
            var languagesDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Languages"));
            if (!Directory.Exists(languagesDir))
                return;

            var requiredKeys = new[]
            {
                "plugin_quickssh_title_commandkeys_install",
                "plugin_quickssh_subtitle_commandkeys_install",
                "plugin_quickssh_keys_install_type_userhost",
                "plugin_quickssh_keys_install_summary",
                "plugin_quickssh_keys_install_run",
                "plugin_quickssh_keys_install_copy_cmd",
                "plugin_quickssh_keys_install_copy_pub",
                "plugin_quickssh_keys_install_copy_cmd_subtitle",
                "plugin_quickssh_keys_install_copy_pub_subtitle",
                "plugin_quickssh_keys_install_alias_notfound",
                "plugin_quickssh_keys_install_pub_notfound",
                "plugin_quickssh_keys_install_pub_invalid",
                "plugin_quickssh_keys_install_invalid_destination",
                "plugin_quickssh_keys_install_copy_cmd_success",
                "plugin_quickssh_keys_install_select_profile",
                "plugin_quickssh_keys_install_manual_destination",
                "plugin_quickssh_keys_install_profile_unsupported",
                "plugin_quickssh_title_commandkeys_manage",
                "plugin_quickssh_subtitle_commandkeys_manage",
                "plugin_quickssh_keys_public_path_label"
            };

            var files = Directory.GetFiles(languagesDir, "*.xaml");
            Assert.Equal(7, files.Length);

            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var file in files)
            {
                var document = XDocument.Load(file);
                var keys = document.Descendants()
                    .Select(element => (string?)element.Attribute(x + "Key"))
                    .OfType<string>()
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var requiredKey in requiredKeys)
                    Assert.Contains(requiredKey, keys);
            }
        }

        [Fact]
        public void AllLanguages_HaveUniqueResourceKeys()
        {
            var languagesDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Languages"));
            if (!Directory.Exists(languagesDir))
                return;

            var files = Directory.GetFiles(languagesDir, "*.xaml");
            Assert.Equal(7, files.Length);

            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var file in files)
            {
                var document = XDocument.Load(file);
                var duplicateKeys = document.Descendants()
                    .Select(element => (string?)element.Attribute(x + "Key"))
                    .OfType<string>()
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .GroupBy(key => key, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray();

                Assert.True(duplicateKeys.Length == 0,
                    $"{Path.GetFileName(file)} contains duplicate localization keys: {string.Join(", ", duplicateKeys)}");
            }
        }
    }
}

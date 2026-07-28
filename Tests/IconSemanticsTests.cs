using System;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.QuickSSH.Tests
{
    public class IconSemanticsTests
    {
        [Theory]
        [InlineData("add")]
        [InlineData("generate")]
        [InlineData("import")]
        [InlineData("export")]
        [InlineData("install")]
        [InlineData("scan")]
        [InlineData("run")]
        [InlineData("saved")]
        public void PositiveOperations_AreGreen(string operation)
        {
            Assert.Equal("Images\\app-green.png", QuickSsh.GetSemanticIconPath(operation));
        }

        [Theory]
        [InlineData("rename")]
        [InlineData("edit")]
        [InlineData("update")]
        public void EditOperations_AreOrange(string operation)
        {
            Assert.Equal("Images\\app-orange.png", QuickSsh.GetSemanticIconPath(operation));
        }

        [Theory]
        [InlineData("remove")]
        [InlineData("delete")]
        public void DestructiveOperations_AreRed(string operation)
        {
            Assert.Equal("Images\\app-red.png", QuickSsh.GetSemanticIconPath(operation));
        }

        [Theory]
        [InlineData("copy")]
        [InlineData("manage")]
        public void NeutralOperations_AreBlue(string operation)
        {
            Assert.Equal("Images\\app.png", QuickSsh.GetSemanticIconPath(operation));
        }

        [Fact]
        public void SemanticIconMapping_IsCentralizedAndUsedByOperationFlows()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var source = File.ReadAllText(Path.Combine(root, "Main.cs"));

            Assert.Contains("internal static string GetSemanticIconPath(string operation)", source);
            Assert.Contains("IcoPath = GetSemanticIconPath(\"rename\")", source);
            Assert.Contains("AppIconRedPath", source);
        }

        [Fact]
        public void OperationRows_UseApprovedSemanticColors()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var source = File.ReadAllText(Path.Combine(root, "Main.cs"));

            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleProfilesAdd", "HandleProfilesRemove"));
            Assert.Contains("IcoPath = AppIconRedPath", ReadMethod(
                source, "HandleProfilesRemove", "HandleProfilesRename"));
            Assert.Contains("IcoPath = GetSemanticIconPath(\"rename\")", ReadMethod(
                source, "HandleProfilesRename", "HandleProfilesCopy"));
            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleProfilesExport", "HandleProfilesImport"));
            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleProfilesImport", "HandleDirectConnect"));

            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleActionsUse", "BuildActionConfirmationResults"));
            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleActionsAdd", "HandleActionsRemove"));
            Assert.Contains("IcoPath = AppIconRedPath", ReadMethod(
                source, "HandleActionsRemove", "HandleActionsRename"));
            Assert.Contains("IcoPath = AppIconOrangePath", ReadMethod(
                source, "HandleActionsRename", "HandleActionsRun"));

            var keyAdd = ReadMethod(source, "HandleKeysAdd", "HandleKeysInstall");
            Assert.Contains("if (!File.Exists(expandedPath))", keyAdd);
            Assert.Contains("IcoPath = AppIconRedPath", keyAdd);
            Assert.Contains("IcoPath = AppIconGreenPath", keyAdd);
            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleKeysInstall", "HandleKeysGenerate"));
            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleKeysGenerate", "HandleKeysRemove"));
            Assert.Contains("IcoPath = GetKeyOperationIconPath(entryValue, \"remove\")", ReadMethod(
                source, "HandleKeysRemove", "HandleKeysRename"));
            Assert.Contains("IcoPath = GetKeyOperationIconPath(entry.Value, \"rename\")", ReadMethod(
                source, "HandleKeysRename", "HandleKeysCopyPath"));
            Assert.Contains("IcoPath = AppIconGreenPath", ReadMethod(
                source, "HandleKeysScan", "HandleConfig"));

            var shellRegion = ReadMethod(source, "HandleShell", "HandleKeys");
            Assert.Contains("IcoPath = AppIconGreenPath", shellRegion);
            Assert.Contains("IcoPath = AppIconRedPath", shellRegion);
        }

        [Fact]
        public void SavedKeysUseContentTypeIconsAndHelpIsNeutralBlue()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var source = File.ReadAllText(Path.Combine(root, "Main.cs"));

            Assert.Contains(
                "private const string PrivateKeyIconPath = \"Images\\\\key-private.png\";",
                source);
            Assert.Contains(
                "private const string PublicKeyIconPath = \"Images\\\\key-public.png\";",
                source);
            Assert.True(File.Exists(Path.Combine(root, "Images", "key-private.png")));
            Assert.True(File.Exists(Path.Combine(root, "Images", "key-public.png")));

            Assert.Contains(
                "private const string PrivateKeyRenameIconPath = \"Images\\\\key-private-rename.png\";",
                source);
            Assert.Contains(
                "private const string PublicKeyRenameIconPath = \"Images\\\\key-public-rename.png\";",
                source);
            Assert.Contains(
                "private const string PrivateKeyRemoveIconPath = \"Images\\\\key-private-remove.png\";",
                source);
            Assert.Contains(
                "private const string PublicKeyRemoveIconPath = \"Images\\\\key-public-remove.png\";",
                source);
            Assert.True(File.Exists(Path.Combine(root, "Images", "key-private-rename.png")));
            Assert.True(File.Exists(Path.Combine(root, "Images", "key-public-rename.png")));
            Assert.True(File.Exists(Path.Combine(root, "Images", "key-private-remove.png")));
            Assert.True(File.Exists(Path.Combine(root, "Images", "key-public-remove.png")));

            Assert.Contains(
                "ProfileWizard.SshKeyFileKind.Private => PrivateKeyRenameIconPath",
                source);
            Assert.Contains(
                "ProfileWizard.SshKeyFileKind.Public => PublicKeyRenameIconPath",
                source);
            Assert.Contains(
                "ProfileWizard.SshKeyFileKind.Private => PrivateKeyRemoveIconPath",
                source);
            Assert.Contains(
                "ProfileWizard.SshKeyFileKind.Public => PublicKeyRemoveIconPath",
                source);

            var keys = ReadMethod(source, "HandleKeysList", "HandleKeysManage");
            Assert.Contains("ProfileWizard.GetKeyFileKind(keyEntry)", keys);
            Assert.Contains("ProfileWizard.SshKeyFileKind.Private => PrivateKeyIconPath", keys);
            Assert.Contains("ProfileWizard.SshKeyFileKind.Public => PublicKeyIconPath", keys);
            Assert.Contains("_ => AppIconRedPath", keys);
            Assert.DoesNotContain("fileExists ? AppIconGreenPath : AppIconRedPath", keys);

            var docsStart = source.IndexOf(
                "private List<Result> HandleDocs(", StringComparison.Ordinal);
            var docsEnd = source.IndexOf("#endregion", docsStart, StringComparison.Ordinal);
            Assert.True(docsStart >= 0 && docsEnd > docsStart);
            var docs = source.Substring(docsStart, docsEnd - docsStart);
            Assert.Contains("IcoPath = AppIconPath", docs);
            Assert.DoesNotContain("IcoPath = AppIconGreenPath", docs);
        }

        [Fact]
        public void KeyOperationRowsKeepExplicitTypeLabels()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var source = File.ReadAllText(Path.Combine(root, "Main.cs"));

            Assert.Contains("private string BuildKeyTypeSubtitle(SshKeyEntry? entry)", source);
            Assert.Contains(
                "GetTranslation(\"plugin_quickssh_keys_private_path_label\")",
                source);
            Assert.Contains(
                "GetTranslation(\"plugin_quickssh_keys_public_path_label\")",
                source);
            Assert.Contains(
                "SubTitle = BuildKeyTypeSubtitle(entryValue)",
                ReadMethod(source, "HandleKeysRemove", "HandleKeysRename"));
            Assert.Contains(
                "SubTitle = BuildKeyTypeSubtitle(entry.Value)",
                ReadMethod(source, "HandleKeysRename", "HandleKeysCopyPath"));
            Assert.Contains("_ => \"\"", source);
            Assert.Contains("keyTypeLabel + \" • \" + displayPath", source);
        }

        [Fact]
        public void KeyManageMenuUsesDedicatedActionIcons()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var source = File.ReadAllText(Path.Combine(root, "Main.cs"));
            var manage = ReadMethod(source, "HandleKeysManage", "HandleKeysAdd");

            var iconFiles = new[]
            {
                "key-manage-add.png",
                "key-manage-generate.png",
                "key-manage-scan.png",
                "key-manage-rename.png",
                "key-manage-copy-path.png",
                "key-manage-copy-public.png",
                "key-manage-remove.png"
            };

            foreach (var iconFile in iconFiles)
                Assert.True(File.Exists(Path.Combine(root, "Images", iconFile)));

            Assert.Contains("KeyManageAddIconPath", manage);
            Assert.Contains("KeyManageGenerateIconPath", manage);
            Assert.Contains("KeyManageScanIconPath", manage);
            Assert.Contains("KeyManageRenameIconPath", manage);
            Assert.Contains("KeyManageCopyPathIconPath", manage);
            Assert.Contains("KeyManageCopyPublicIconPath", manage);
            Assert.Contains("KeyManageRemoveIconPath", manage);

            Assert.DoesNotContain("AppIconGreenPath", manage);
            Assert.DoesNotContain("AppIconOrangePath", manage);
            Assert.DoesNotContain("AppIconRedPath", manage);
            Assert.DoesNotContain("AppIconPath", manage);
        }

        [Fact]
        public void CopyExecutionRows_RemainNeutralBlue()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var source = File.ReadAllText(Path.Combine(root, "Main.cs"));

            Assert.Contains("IcoPath = GetSemanticIconPath(\"copy\")", ReadMethod(
                source, "HandleProfilesCopy", "HandleProfilesExport"));
            Assert.Contains("IcoPath = GetSemanticIconPath(\"copy\")", ReadMethod(
                source, "HandleKeysCopyPath", "HandleKeysCopyPub"));
            Assert.Contains("IcoPath = GetSemanticIconPath(\"copy\")", ReadMethod(
                source, "HandleKeysCopyPub", "HandleKeysScan"));
        }

        private static void AssertMethodContainsIcon(
            string source,
            string methodName,
            string nextMethodName,
            string operation)
        {
            var region = ReadMethod(source, methodName, nextMethodName);
            Assert.Contains($"IcoPath = GetSemanticIconPath(\"{operation}\")", region);
        }

        private static void AssertMethodFirstIcon(
            string source,
            string methodName,
            string nextMethodName,
            string operation)
        {
            var region = ReadMethod(source, methodName, nextMethodName);
            var iconStart = region.IndexOf("IcoPath = ", StringComparison.Ordinal);
            Assert.True(iconStart >= 0, $"No icon found in {methodName}.");

            var iconEnd = region.IndexOf('\n', iconStart);
            var iconLine = iconEnd >= 0
                ? region.Substring(iconStart, iconEnd - iconStart)
                : region.Substring(iconStart);

            Assert.Equal($"IcoPath = GetSemanticIconPath(\"{operation}\"),", iconLine.Trim());
        }

        private static string ReadMethod(string source, string methodName, string nextMethodName)
        {
            var start = source.IndexOf(
                $"private List<Result> {methodName}(", StringComparison.Ordinal);
            var end = source.IndexOf(
                $"private List<Result> {nextMethodName}(", start, StringComparison.Ordinal);

            Assert.True(start >= 0, $"Method {methodName} was not found.");
            Assert.True(end > start, $"Method boundary {nextMethodName} was not found.");
            return source.Substring(start, end - start);
        }

        [Fact]
        public void Autocomplete_UsesTheSameSemanticIconMapping()
        {
            var root = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            var source = File.ReadAllText(Path.Combine(root, "AutoCompleter.cs"));

            Assert.Contains("QuickSsh.GetSemanticIconPath(sub)", source);
            Assert.Contains("QuickSsh.GetSemanticIconPath(\"saved\")", source);
        }
    }
}

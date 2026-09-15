using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Exercises the autostart helpers against a scratch Run key. Tests in one class share an xunit
    /// collection and so run sequentially, which is what makes setting the static seam safe here.
    /// </summary>
    public class AutorunTests : IDisposable
    {
        private const string KeyName = "Screenzap";
        private const string ScratchRoot = @"Software\Screenzap.AutorunTests";

        private readonly string scratchLocation;
        private readonly string? previousLocation;
        private readonly List<string> tempFiles = new();

        public AutorunTests()
        {
            scratchLocation = $@"{ScratchRoot}\{Guid.NewGuid():N}";
            previousLocation = Util.RunLocationForDiagnostics;
            Util.RunLocationForDiagnostics = scratchLocation;
            Registry.CurrentUser.CreateSubKey(scratchLocation)?.Dispose();
        }

        public void Dispose()
        {
            Util.RunLocationForDiagnostics = previousLocation;

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(scratchLocation, throwOnMissingSubKey: false);

                // Drop the test-owned root too, once the last test in the class has left it empty.
                using var root = Registry.CurrentUser.OpenSubKey(ScratchRoot, writable: false);
                if (root != null && root.SubKeyCount == 0 && root.ValueCount == 0)
                {
                    root.Dispose();
                    Registry.CurrentUser.DeleteSubKey(ScratchRoot, throwOnMissingSubKey: false);
                }
            }
            catch (Exception)
            {
                // Scratch cleanup is best-effort; a leftover empty key must not fail a run.
            }

            foreach (var file in tempFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception)
                {
                    // Same: best-effort.
                }
            }

            GC.SuppressFinalize(this);
        }

        [Fact]
        public void IsAutoStartEnabled_IsFalse_WhenNoValuePresent()
        {
            Assert.False(Util.IsAutoStartEnabled(KeyName));
        }

        [Fact]
        public void IsAutoStartEnabled_IsFalse_WhenValueIsBlank()
        {
            WriteRaw("   ");

            Assert.False(Util.IsAutoStartEnabled(KeyName));
        }

        [Fact]
        public void SetAutoStart_ThenUnSetAutoStart_RoundTrips()
        {
            var command = Quote(CreateTempExe());

            Util.SetAutoStart(KeyName, command);
            Assert.True(Util.IsAutoStartEnabled(KeyName));
            Assert.Equal(command, ReadRaw());

            Util.UnSetAutoStart(KeyName);
            Assert.False(Util.IsAutoStartEnabled(KeyName));
            Assert.Null(ReadRaw());
        }

        [Fact]
        public void UnSetAutoStart_IsQuiet_WhenNoValuePresent()
        {
            Util.UnSetAutoStart(KeyName);

            Assert.False(Util.IsAutoStartEnabled(KeyName));
        }

        /// <summary>
        /// The regression this API was reshaped for: the toggle used to compare the stored path
        /// against the running copy, so launching a second copy reported "off" while autostart was
        /// in fact configured -- and clicking it then repointed or cleared a working entry.
        /// </summary>
        [Fact]
        public void IsAutoStartEnabled_IsTrue_WhenValuePointsAtADifferentCopy()
        {
            WriteRaw(Quote(@"D:\projects\code\screenzap\screenzap\bin\Release\Screenzap.exe"));

            Assert.True(Util.IsAutoStartEnabled(KeyName));
        }

        [Fact]
        public void RepairAutoStartTarget_DoesNotCreate_WhenNoValuePresent()
        {
            var repaired = Util.RepairAutoStartTarget(KeyName, Quote(CreateTempExe()));

            Assert.False(repaired);
            Assert.Null(ReadRaw());
            Assert.False(Util.IsAutoStartEnabled(KeyName));
        }

        [Fact]
        public void RepairAutoStartTarget_RewritesLegacyDllTarget()
        {
            // Assembly.Location under .NET yields the managed dll, which Windows cannot launch.
            WriteRaw(@"C:\Users\someone\AppData\Local\Programs\Screenzap\screenzap.dll");
            var command = Quote(CreateTempExe());

            var repaired = Util.RepairAutoStartTarget(KeyName, command);

            Assert.True(repaired);
            Assert.Equal(command, ReadRaw());
        }

        [Fact]
        public void RepairAutoStartTarget_RewritesTargetThatIsGoneFromDisk()
        {
            var missing = Path.Combine(Path.GetTempPath(), $"screenzap-{Guid.NewGuid():N}.exe");
            WriteRaw(Quote(missing));
            var command = Quote(CreateTempExe());

            var repaired = Util.RepairAutoStartTarget(KeyName, command);

            Assert.True(repaired);
            Assert.Equal(command, ReadRaw());
        }

        /// <summary>
        /// A real exe that is simply not this copy is someone's deliberate choice, so running a dev
        /// build must not silently steal a working install's autostart entry.
        /// </summary>
        [Fact]
        public void RepairAutoStartTarget_LeavesAnotherInstallAlone()
        {
            var otherInstall = Quote(CreateTempExe());
            WriteRaw(otherInstall);

            var repaired = Util.RepairAutoStartTarget(KeyName, Quote(CreateTempExe()));

            Assert.False(repaired);
            Assert.Equal(otherInstall, ReadRaw());
        }

        [Fact]
        public void RepairAutoStartTarget_ReadsUnquotedTargets()
        {
            var otherInstall = CreateTempExe();
            WriteRaw(otherInstall);

            var repaired = Util.RepairAutoStartTarget(KeyName, Quote(CreateTempExe()));

            Assert.False(repaired);
            Assert.Equal(otherInstall, ReadRaw());
        }

        private static string Quote(string path) => $"\"{path}\"";

        private string CreateTempExe()
        {
            var path = Path.Combine(Path.GetTempPath(), $"screenzap-test-{Guid.NewGuid():N}.exe");
            File.WriteAllBytes(path, Array.Empty<byte>());
            tempFiles.Add(path);
            return path;
        }

        private void WriteRaw(string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(scratchLocation);
            Assert.NotNull(key);
            key!.SetValue(KeyName, value);
        }

        private string? ReadRaw()
        {
            using var key = Registry.CurrentUser.OpenSubKey(scratchLocation, writable: false);
            return key?.GetValue(KeyName) as string;
        }
    }
}

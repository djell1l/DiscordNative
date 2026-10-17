using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

[assembly: AssemblyTitle("Diswin Setup")]
[assembly: AssemblyDescription("Installateur Diswin")]
[assembly: AssemblyCompany("Diswin")]
[assembly: AssemblyProduct("Diswin")]
[assembly: AssemblyVersion("0.3.3.0")]
[assembly: AssemblyFileVersion("0.3.3.0")]

namespace Diswin.SetupBootstrapper
{
    internal static class Program
    {
        private static readonly byte[] MagicMarker = new byte[] { 0x44, 0x49, 0x53, 0x57, 0x49, 0x4E, 0x53, 0x46, 0x58 }; // "DISWINSFX"

        [STAThread]
        private static int Main(string[] args)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DiswinSetup_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string selfPath = Process.GetCurrentProcess().MainModule.FileName;
                Directory.CreateDirectory(tempDir);

                using (var fs = new FileStream(selfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 17)
                        return 1;

                    fs.Seek(-17, SeekOrigin.End);
                    byte[] footer = new byte[17];
                    int read = fs.Read(footer, 0, 17);
                    if (read != 17)
                        return 1;

                    for (int i = 0; i < MagicMarker.Length; i++)
                    {
                        if (footer[8 + i] != MagicMarker[i])
                            return 1;
                    }

                    long zipLength = BitConverter.ToInt64(footer, 0);
                    long zipStart = fs.Length - 17 - zipLength;
                    if (zipStart <= 0 || zipLength <= 0)
                        return 1;

                    string tempZip = Path.Combine(tempDir, "bundle.zip");
                    fs.Seek(zipStart, SeekOrigin.Begin);
                    using (var outZip = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long remaining = zipLength;
                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(buffer.Length, remaining);
                            int n = fs.Read(buffer, 0, toRead);
                            if (n <= 0) break;
                            outZip.Write(buffer, 0, n);
                            remaining -= n;
                        }
                    }

                    ZipFile.ExtractToDirectory(tempZip, tempDir);
                    File.Delete(tempZip);
                }

                string setupExe = Path.Combine(tempDir, "Setup.exe");
                if (!File.Exists(setupExe))
                    return 1;

                var psi = new ProcessStartInfo(setupExe)
                {
                    WorkingDirectory = tempDir,
                    UseShellExecute = true
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();
                }
                return 0;
            }
            catch
            {
                return 1;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }
    }
}

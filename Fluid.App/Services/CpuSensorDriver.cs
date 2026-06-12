using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Fluid.App.Services;

/// <summary>
/// v1.25: orchestrates the OPTIONAL CPU-temperature sensor driver (PawnIO).
///
/// fluidMonitor never bundles or redistributes the driver. When the user
/// explicitly opts in, this downloads the official signed installer from its
/// own release URL, verifies the Authenticode signature, and runs it silently.
/// The user sees exactly one Windows UAC prompt (driver install requires
/// elevation by Windows design — unavoidable for any hardware component).
///
/// Every step is defensive: any failure (no network, moved URL, bad signature,
/// AV quarantine) returns a Failed result with a reason, and the dialog falls
/// back to a manual "download it yourself" link. The app's core never depends
/// on this succeeding.
/// </summary>
public static class CpuSensorDriver
{
    // Official signed installer. Single source of truth — change here only.
    public const string DownloadUrl =
        "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    public const string HomePageUrl = "https://pawnio.eu/";
    public const string SourceUrl   = "https://github.com/namazso/PawnIO";

    // The driver's uninstall registry key (64-bit view), used for both
    // presence detection and locating the uninstaller for opt-out.
    private const string UninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public enum Result { Installed, AlreadyPresent, Cancelled, Failed }

    public sealed record Outcome(Result Result, string? Detail = null);

    /// <summary>True when the sensor driver is installed on this machine.</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.RegistryKey
                .OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                             Microsoft.Win32.RegistryView.Registry64)
                .OpenSubKey(UninstallKeyPath);
            return key?.GetValue("DisplayVersion") is string v && v.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Download → verify signature → silent elevated install. The single UAC
    /// prompt fires when the installer launches with Verb="runas". Returns an
    /// Outcome the dialog turns into either success or the manual fallback.
    /// </summary>
    public static async Task<Outcome> InstallAsync()
    {
        if (IsInstalled())
            return new Outcome(Result.AlreadyPresent);

        string tempPath = Path.Combine(Path.GetTempPath(),
            $"fluidMonitor_sensor_{Guid.NewGuid():N}.exe");

        try
        {
            // 1. Download.
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("fluidMonitor");
                var bytes = await http.GetByteArrayAsync(DownloadUrl);
                await File.WriteAllBytesAsync(tempPath, bytes);
            }

            // 2. Verify the Authenticode signature BEFORE running anything.
            //    A driver installer that isn't validly signed is never run.
            if (!IsAuthenticodeValid(tempPath, out var signer))
            {
                TryDelete(tempPath);
                return new Outcome(Result.Failed, "The downloaded installer's signature could not be verified.");
            }

            // 3. Silent elevated install. Verb=runas triggers the one UAC prompt.
            //    "-install -silent" are PawnIO's own switches (no wizard window).
            var psi = new ProcessStartInfo
            {
                FileName        = tempPath,
                Arguments       = "-install -silent",
                UseShellExecute = true,     // required for Verb=runas
                Verb            = "runas",
                WindowStyle     = ProcessWindowStyle.Hidden,
            };

            using (var proc = Process.Start(psi))
            {
                if (proc == null)
                {
                    TryDelete(tempPath);
                    return new Outcome(Result.Failed, "The installer could not be started.");
                }
                await proc.WaitForExitAsync();
            }

            TryDelete(tempPath);

            // 4. Confirm it actually landed.
            return IsInstalled()
                ? new Outcome(Result.Installed, signer)
                : new Outcome(Result.Failed, "The installer ran but the driver was not detected afterward.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC elevation prompt.
            TryDelete(tempPath);
            return new Outcome(Result.Cancelled);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            return new Outcome(Result.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Opt-out: run the driver's own uninstaller (one UAC prompt). The app is
    /// unaffected either way; the CPU tile simply returns to the opt-in hint.
    /// </summary>
    public static async Task<Outcome> UninstallAsync()
    {
        if (!IsInstalled())
            return new Outcome(Result.AlreadyPresent);

        try
        {
            string? uninstallCmd = null;
            using (var key = Microsoft.Win32.RegistryKey
                .OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                             Microsoft.Win32.RegistryView.Registry64)
                .OpenSubKey(UninstallKeyPath))
            {
                uninstallCmd = (key?.GetValue("QuietUninstallString") as string)
                               ?? (key?.GetValue("UninstallString") as string);
            }

            if (string.IsNullOrWhiteSpace(uninstallCmd))
                return new Outcome(Result.Failed, "Could not locate the driver's uninstaller.");

            // UninstallString may be "\"C:\\path\\unins.exe\" /flags". Split the
            // quoted exe from its arguments for ProcessStartInfo.
            string exe, args;
            if (uninstallCmd.StartsWith("\""))
            {
                int end = uninstallCmd.IndexOf('"', 1);
                exe  = uninstallCmd.Substring(1, end - 1);
                args = uninstallCmd.Substring(end + 1).Trim();
            }
            else
            {
                var sp = uninstallCmd.IndexOf(' ');
                exe  = sp < 0 ? uninstallCmd : uninstallCmd.Substring(0, sp);
                args = sp < 0 ? "" : uninstallCmd.Substring(sp + 1).Trim();
            }
            if (!args.Contains("silent", StringComparison.OrdinalIgnoreCase))
                args = ("-uninstall -silent " + args).Trim();

            var psi = new ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                UseShellExecute = true,
                Verb            = "runas",
                WindowStyle     = ProcessWindowStyle.Hidden,
            };
            using (var proc = Process.Start(psi))
            {
                if (proc != null) await proc.WaitForExitAsync();
            }

            return IsInstalled()
                ? new Outcome(Result.Failed, "The uninstaller ran but the driver is still present.")
                : new Outcome(Result.Installed); // "Installed" here = state changed OK
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new Outcome(Result.Cancelled);
        }
        catch (Exception ex)
        {
            return new Outcome(Result.Failed, ex.Message);
        }
    }

    private static bool IsAuthenticodeValid(string path, out string signer)
    {
        signer = "";
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            signer = cert.GetNameInfo(X509NameType.SimpleName, false);

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            bool ok = chain.Build(cert);
            if (ok) return true;

            // Allow an offline machine to still validate the chain structure;
            // reject only on genuine trust failures.
            foreach (var status in chain.ChainStatus)
            {
                if (status.Status != X509ChainStatusFlags.NoError &&
                    status.Status != X509ChainStatusFlags.RevocationStatusUnknown &&
                    status.Status != X509ChainStatusFlags.OfflineRevocation)
                    return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

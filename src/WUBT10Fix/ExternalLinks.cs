using System.Diagnostics;

namespace WUBT10Fix;

internal static class ExternalLinks
{
    internal const string WindowsMidiServices = "https://microsoft.github.io/MIDI/get-latest/";
    internal const string SupportOnce = "https://ko-fi.com/wubt10fix";
    internal const string SupportMonthly = "https://ko-fi.com/wubt10fix/tiers";

    private static readonly HashSet<string> AllowedUrls =
    [
        WindowsMidiServices,
        SupportOnce,
        SupportMonthly
    ];

    internal static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && AllowedUrls.Contains(uri.AbsoluteUri);

    internal static bool TryOpen(
        string url,
        Action<ProcessStartInfo>? launch = null)
    {
        if (!IsAllowed(url))
        {
            RepairLog.Write("Blocked an untrusted external URL.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(url) { UseShellExecute = true };
            if (launch is null)
                _ = Process.Start(startInfo);
            else
                launch(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            RepairLog.Write("Could not open URL: " + ex);
            return false;
        }
    }
}

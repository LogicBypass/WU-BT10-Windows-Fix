using System.Reflection;

namespace WUBT10Fix;

internal static class SupportOptions
{
    internal static string? SupportUrl => Metadata("SupportUrl");
    internal static string? RepositoryUrl => Metadata("RepositoryUrl");

    private static string? Metadata(string key)
    {
        var value = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }
}

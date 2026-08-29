using System.Reflection;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Compares this MCP server's version against the version of the plugin sources installed in the
/// Sitefinity project (reported by <c>GET /mcp/ping</c>) and turns the comparison into a verdict the
/// operator can act on.
/// <para>
/// The rule everywhere: never say only "out of date". Every mismatch message carries the exact steps
/// that fix it, because the two halves are updated by completely different commands — the plugin by
/// re-running <c>install-plugin.ps1</c> and rebuilding the Sitefinity solution, the server by an npm
/// update and a client restart.
/// </para>
/// </summary>
public static class PluginVersionAdvisor
{
    /// <summary>Shown when the plugin predates version reporting (any build before 3.6.0).</summary>
    public const string UnknownPluginVersion = "3.5.0 or earlier (no version reported)";

    private static readonly Lazy<string> ServerVersionValue = new(ReadServerVersion);

    /// <summary>
    /// The verdict line(s) for a plugin/server version pair. Pass <c>null</c> for
    /// <paramref name="pluginVersion"/> when the ping reported none.
    /// </summary>
    /// <param name="pluginVersion">Version the plugin reported, or null.</param>
    /// <param name="serverVersion">Version of this MCP server. Defaults to the running assembly's.</param>
    public static string BuildVerdict(string? pluginVersion, string? serverVersion = null)
    {
        var server = string.IsNullOrWhiteSpace(serverVersion) ? ServerVersion : serverVersion!;

        if (string.IsNullOrWhiteSpace(pluginVersion))
        {
            return "Site plugin is 3.5.0 or earlier (it predates version reporting). If tools fail with " +
                   "404s, update the plugin: clone or pull github.com/sitefinitysteve/SitefinityCommunity.Mcp " +
                   $"at tag v{server}, run .\\install-plugin.ps1 -Target \"<path to your Sitefinity web " +
                   "project>\", rebuild the Sitefinity solution, then recycle the app pool.";
        }

        var comparison = Compare(pluginVersion!, server);

        if (comparison == 0)
        {
            return $"Plugin and server versions match (v{server}).";
        }

        if (comparison < 0)
        {
            return BuildPluginOlderMessage(pluginVersion!, server);
        }

        return $"MCP server v{server} is older than the site plugin v{pluginVersion}. Update the server: " +
               "npm install -g sitefinity-comm-mcp@latest (or update the version pinned in your MCP client " +
               "config), then restart your MCP client/session.";
    }

    /// <summary>
    /// The multi-step message for a plugin that is behind the server. Used by the status verdict and,
    /// compressed, by the 404 mapping.
    /// </summary>
    /// <param name="pluginVersion">Version the plugin reported.</param>
    /// <param name="serverVersion">Version of this MCP server.</param>
    public static string BuildPluginOlderMessage(string pluginVersion, string serverVersion)
    {
        return $"Site plugin v{pluginVersion} is older than the MCP server v{serverVersion}. To update: " +
               "(1) get the matching plugin sources — clone or pull " +
               $"github.com/sitefinitysteve/SitefinityCommunity.Mcp at tag v{serverVersion}; " +
               "(2) run .\\install-plugin.ps1 -Target \"<path to your Sitefinity web project>\" (copies the " +
               ".cs files and registers new ones in the .csproj); (3) rebuild the Sitefinity solution; " +
               "(4) recycle the app pool. New endpoints 404 until this is done.";
    }

    /// <summary>
    /// One-sentence form for an endpoint that answered 404 — the same fix, compressed to fit inside an
    /// error message. Names both versions when the plugin reported one.
    /// </summary>
    /// <param name="endpoint">The plugin route that returned 404, e.g. <c>/RestApi/mcp/search-indexes</c>.</param>
    /// <param name="pluginVersion">Version the plugin reported, or null when it reported none.</param>
    public static string BuildNotFoundMessage(string endpoint, string? pluginVersion)
    {
        var server = ServerVersion;
        var versions = string.IsNullOrWhiteSpace(pluginVersion)
            ? $"plugin predates version reporting (3.5.0 or earlier), MCP server v{server}"
            : $"plugin v{pluginVersion}, MCP server v{server}";

        return $"The {endpoint} endpoint returned 404 — the installed Sitefinity plugin is out of date " +
               $"({versions}). Pull github.com/sitefinitysteve/SitefinityCommunity.Mcp at tag v{server}, run " +
               ".\\install-plugin.ps1 -Target \"<path to your Sitefinity web project>\", rebuild the " +
               "Sitefinity solution, then recycle the app pool.";
    }

    /// <summary>
    /// Compares two dotted version strings numerically, ignoring any pre-release suffix. Returns a
    /// negative number when <paramref name="left"/> is older. Unparsable input compares as equal, so a
    /// malformed version never produces a false "out of date" claim.
    /// </summary>
    /// <param name="left">First version.</param>
    /// <param name="right">Second version.</param>
    public static int Compare(string left, string right)
    {
        var leftParts = Parse(left);
        var rightParts = Parse(right);

        if (leftParts is null || rightParts is null)
        {
            return 0;
        }

        for (var i = 0; i < 3; i++)
        {
            if (leftParts[i] != rightParts[i])
            {
                return leftParts[i].CompareTo(rightParts[i]);
            }
        }

        return 0;
    }

    private static int[]? Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        // Drop a pre-release / build suffix: 3.6.0-beta.1 compares as 3.6.0.
        var core = version.Trim().Split('-', '+')[0];
        var segments = core.Split('.');
        var parts = new int[3];

        for (var i = 0; i < 3; i++)
        {
            if (i >= segments.Length)
            {
                parts[i] = 0;
                continue;
            }

            if (!int.TryParse(segments[i], out var value))
            {
                return null;
            }

            parts[i] = value;
        }

        return parts;
    }

    private static string ReadServerVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit sha>" to the informational version.
            return informational!.Split('+')[0];
        }

        var version = assembly.GetName().Version;

        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Version of this MCP server, read once from the running assembly.</summary>
    public static string ServerVersion => ServerVersionValue.Value;
}

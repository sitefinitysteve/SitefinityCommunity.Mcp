using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for discovering configured environments and switching the active default.
/// These are local-only (operate on the in-memory <see cref="IEnvironmentResolver"/>) and do
/// not require Sitefinity to be reachable.
/// </summary>
[McpServerToolType]
public sealed class EnvironmentTools
{
    private readonly IEnvironmentResolver _resolver;

    public EnvironmentTools(IEnvironmentResolver resolver)
    {
        this._resolver = resolver;
    }

    [McpServerTool(Name = "sitefinity_list_environments", Title = "List Environments", ReadOnly = true)]
    [Description("Show all configured Sitefinity environments, which is the current default, and whether each uses local or remote log access.")]
    public string ListEnvironments()
    {
        var environments = this._resolver.GetAll();
        var defaultEnv = this._resolver.DefaultEnvironment;

        var sb = new StringBuilder();
        sb.AppendLine("Configured Sitefinity environments:");
        sb.AppendLine();

        foreach (var (name, config) in environments)
        {
            var isDefault = string.Equals(name, defaultEnv, StringComparison.OrdinalIgnoreCase);
            var mode = config.IsLocalMode ? "local" : "remote";
            var marker = isDefault ? " (active default)" : "";

            sb.AppendLine($"  {name}{marker}");
            sb.AppendLine($"    URL:  {config.Url}");
            sb.AppendLine($"    Logs: {mode}{(config.IsLocalMode ? $" ({config.LogsPath})" : "")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "sitefinity_set_default_environment", Title = "Set Default Environment")]
    [Description("Switch the active default environment. All subsequent tool calls without an explicit environment parameter will target this environment.")]
    public string SetDefaultEnvironment(
        [Description("The environment name to set as default (e.g., 'dev', 'staging', 'prod')")] string environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            return "Error: environment name cannot be empty.";
        }

        if (this._resolver.SetDefault(environment))
        {
            var (_, config) = this._resolver.Resolve(environment);
            return $"Default environment switched to '{environment}' ({config.Url}). " +
                   $"Log mode: {(config.IsLocalMode ? "local" : "remote")}.";
        }

        var available = string.Join(", ", this._resolver.GetAll().Keys);
        return $"Unknown environment '{environment}'. Available: {available}";
    }
}

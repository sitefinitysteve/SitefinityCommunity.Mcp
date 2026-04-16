using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for Sitefinity forms: listing forms, fetching their field definitions, and paging
/// through submitted responses. Form responses are secret-redacted on the plugin side before transit.
/// </summary>
[McpServerToolType]
public sealed class FormTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public FormTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_forms", ReadOnly = true)]
    [Description("List all Sitefinity forms (Id, Name, Title, FieldCount, EntryCount). Use to discover forms " +
                 "before calling sitefinity_get_form_fields or sitefinity_list_form_responses.")]
    public async Task<string> ListForms(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var response = await this._metadataService.ListFormsAsync(environment, ct);
            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "sitefinity_get_form_fields", ReadOnly = true)]
    [Description("Get field definitions for a specific form. Returns each field's developer name " +
                 "(the FieldName used by the Sitefinity API for entry values, e.g. \"FormTextBox_C001\"), " +
                 "its display Title, FieldType, IsRequired flag, and Choices. " +
                 "Call sitefinity_list_forms first to discover form Ids or Names. " +
                 "Pass debug=true to include a raw dump of Sitefinity's internal Properties tree — use this " +
                 "to troubleshoot empty Name/Title results on unfamiliar Sitefinity versions.")]
    public async Task<string> GetFormFields(
        [Description("Form identifier: Guid or form Name")]
        string formIdentifier,
        [Description("When true, includes a raw Properties/ChildProperties tree dump for diagnostics. Default false.")]
        bool debug = false,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(formIdentifier))
        {
            return "Error: formIdentifier is required.";
        }

        try
        {
            var response = await this._metadataService.GetFormFieldsAsync(formIdentifier, debug, environment, ct);
            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "sitefinity_list_form_responses", ReadOnly = true)]
    [Description("List form submissions for a given form, ordered newest-first. Pass a searchTerm to return " +
                 "only entries where any field value (or IP / UserAgent) contains that term (case-insensitive). " +
                 "Field values whose names look like credentials (password, apiKey, secret, token, etc.) are " +
                 "redacted before return AND before search matching, so sensitive values cannot leak via search. " +
                 "Call sitefinity_list_forms first to discover form Ids or Names.")]
    public async Task<string> ListFormResponses(
        [Description("Form identifier: Guid or form Name")]
        string formIdentifier,
        [Description("Case-insensitive substring to match across every field value on each entry. Leave empty to list all.")]
        string? searchTerm = null,
        [Description("Max responses to return. Default 50, max 500.")]
        int take = 50,
        [Description("Responses to skip for paging (applied after search filtering). Default 0.")]
        int skip = 0,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(formIdentifier))
        {
            return "Error: formIdentifier is required.";
        }

        if (take <= 0)
        {
            take = 50;
        }

        if (take > 500)
        {
            take = 500;
        }

        if (skip < 0)
        {
            skip = 0;
        }

        try
        {
            var response = await this._metadataService.ListFormResponsesAsync(formIdentifier, take, skip, searchTerm, environment, ct);
            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}

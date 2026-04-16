using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class FormToolsUnitTests
{
    private static (FormTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new FormTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task ListForms_ReturnsJsonWithForms()
    {
        var (tools, mock) = CreateTools();
        mock.ListFormsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FormsResponse
            {
                Forms =
                [
                    new FormInfo { Id = "f-1", Name = "Contact", Title = "Contact Us", FieldCount = 4, EntryCount = 12, IsPublished = true },
                    new FormInfo { Id = "f-2", Name = "Newsletter", Title = "Newsletter", FieldCount = 1, EntryCount = 300, IsPublished = true },
                ],
            });

        var result = await tools.ListForms();

        Assert.Contains("Contact Us", result);
        Assert.Contains("Newsletter", result);
        Assert.Contains("\"EntryCount\": 300", result);
    }

    [Fact]
    public async Task GetFormFields_ReturnsFieldsWithChoices()
    {
        var (tools, mock) = CreateTools();
        mock.GetFormFieldsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FormFieldsResponse
            {
                FormId = "f-1",
                FormName = "Contact",
                FormTitle = "Contact Us",
                Fields =
                [
                    new FormFieldInfo { Name = "Name", Title = "Your Name", FieldType = "TextFieldController", IsRequired = true },
                    new FormFieldInfo
                    {
                        Name = "Reason",
                        Title = "Reason",
                        FieldType = "DropdownListFieldController",
                        Choices = ["Sales", "Support", "Other"],
                    },
                ],
            });

        var result = await tools.GetFormFields("Contact");

        Assert.Contains("Your Name", result);
        Assert.Contains("DropdownListFieldController", result);
        Assert.Contains("Sales", result);
        Assert.Contains("Support", result);
    }

    [Fact]
    public async Task GetFormFields_RejectsEmptyIdentifier()
    {
        var (tools, _) = CreateTools();

        var result = await tools.GetFormFields("");

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task ListFormResponses_ReturnsEntries()
    {
        var (tools, mock) = CreateTools();
        mock.ListFormResponsesAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FormResponsesResponse
            {
                FormId = "f-1",
                FormName = "Contact",
                TotalCount = 1,
                Take = 50,
                Skip = 0,
                Responses =
                [
                    new FormResponseInfo
                    {
                        Id = "entry-1",
                        SubmittedOn = new DateTime(2026, 4, 1),
                        Values = new Dictionary<string, string>
                        {
                            ["Name"] = "Ada",
                            // Plugin should have already redacted these before returning
                            ["Password"] = "[REDACTED]",
                        },
                    },
                ],
            });

        var result = await tools.ListFormResponses("Contact");

        Assert.Contains("Ada", result);
        Assert.Contains("[REDACTED]", result);
        Assert.DoesNotContain("Hunter2", result);
    }

    [Fact]
    public async Task ListFormResponses_PassesTakeSkip()
    {
        var (tools, mock) = CreateTools();
        mock.ListFormResponsesAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new FormResponsesResponse());

        await tools.ListFormResponses("Contact", take: 20, skip: 5);

        await mock.Received(1).ListFormResponsesAsync(
            "Contact", 20, 5, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListForms_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.ListFormsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("unreachable"));

        var result = await tools.ListForms();

        Assert.StartsWith("Error:", result);
    }
}

using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ContentToolsUnitTests
{
    private static (ContentTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new ContentTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task ListContent_ReturnsJsonWithItems()
    {
        var (tools, mock) = CreateTools();
        mock.ListContentAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentListResponse
            {
                TypeFullName = "Telerik.Sitefinity.News.Model.NewsItem",
                TotalCount = 2,
                Take = 50,
                Skip = 0,
                Items =
                [
                    new ContentItemInfo { Id = "id-1", Title = "Hello", UrlName = "hello", Status = "Live" },
                    new ContentItemInfo { Id = "id-2", Title = "World", UrlName = "world", Status = "Live" },
                ],
            });

        var result = await tools.ListContent("Telerik.Sitefinity.News.Model.NewsItem");

        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
        Assert.Contains("NewsItem", result);
        Assert.Contains("\"TotalCount\": 2", result);
    }

    [Fact]
    public async Task ListContent_HandlesEmptyList()
    {
        var (tools, mock) = CreateTools();
        mock.ListContentAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentListResponse
            {
                TypeFullName = "Some.Type",
                TotalCount = 0,
            });

        var result = await tools.ListContent("Some.Type");

        Assert.Contains("\"TotalCount\": 0", result);
        Assert.Contains("\"Items\": []", result);
    }

    [Fact]
    public async Task ListContent_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.ListContentAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await tools.ListContent("Some.Type");

        Assert.StartsWith("Error:", result);
        Assert.Contains("Connection refused", result);
    }

    [Fact]
    public async Task ListContent_PassesTakeSkip()
    {
        var (tools, mock) = CreateTools();
        mock.ListContentAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ContentListResponse());

        await tools.ListContent("Some.Type", take: 25, skip: 100);

        await mock.Received(1).ListContentAsync(
            "Some.Type", 25, 100, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListContent_RejectsEmptyType()
    {
        var (tools, _) = CreateTools();

        var result = await tools.ListContent("");

        Assert.StartsWith("Error:", result);
        Assert.Contains("typeFullName", result);
    }
}

using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class TaxonomyToolsUnitTests
{
    private static (TaxonomyTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new TaxonomyTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task ListTaxonomies_ReturnsFlatAndHierarchical()
    {
        var (tools, mock) = CreateTools();
        mock.ListTaxonomiesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TaxonomiesResponse
            {
                Taxonomies =
                [
                    new TaxonomyInfo { Id = "cat-id", Name = "Categories", Title = "Categories", TaxonomyType = "Hierarchical", TaxaCount = 3 },
                    new TaxonomyInfo { Id = "tag-id", Name = "Tags", Title = "Tags", TaxonomyType = "Flat", TaxaCount = 12 },
                ],
            });

        var result = await tools.ListTaxonomies();

        Assert.Contains("Categories", result);
        Assert.Contains("Tags", result);
        Assert.Contains("Hierarchical", result);
        Assert.Contains("Flat", result);
    }

    [Fact]
    public async Task ListTaxonomies_IncludesTopTaxa()
    {
        var (tools, mock) = CreateTools();
        mock.ListTaxonomiesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TaxonomiesResponse
            {
                Taxonomies =
                [
                    new TaxonomyInfo { Id = "cat-id", Name = "Categories", Title = "Categories", TaxonomyType = "Hierarchical", TaxaCount = 2 },
                ],
                Taxa = new Dictionary<string, List<TaxonInfo>>
                {
                    ["cat-id"] =
                    [
                        new TaxonInfo { Id = "t1", Name = "news", Title = "News" },
                        new TaxonInfo { Id = "t2", Name = "events", Title = "Events" },
                    ],
                },
            });

        var result = await tools.ListTaxonomies();

        Assert.Contains("cat-id", result);
        Assert.Contains("News", result);
        Assert.Contains("Events", result);
    }

    [Fact]
    public async Task ListTaxonomies_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.ListTaxonomiesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Boom"));

        var result = await tools.ListTaxonomies();

        Assert.StartsWith("Error:", result);
        Assert.Contains("Boom", result);
    }
}

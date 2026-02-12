using System.Net;
using System.Text;

namespace SitefinityCommunity.Mcp.Tests.Helpers;

/// <summary>
/// A test double for HttpMessageHandler that returns a preconfigured response.
/// Pass this to HttpClient's constructor to fake HTTP calls in unit tests.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;
    private readonly string _contentType;

    public HttpRequestMessage? LastRequest { get; private set; }
    public int CallCount { get; private set; }

    public MockHttpMessageHandler(
        string content,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "application/json")
    {
        this._content = content;
        this._statusCode = statusCode;
        this._contentType = contentType;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this.LastRequest = request;
        this.CallCount++;

        var response = new HttpResponseMessage(this._statusCode)
        {
            Content = new StringContent(this._content, Encoding.UTF8, this._contentType),
            RequestMessage = request
        };

        return Task.FromResult(response);
    }
}

using SitefinityCommunity.Mcp.Security;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SecretRedactorTests
{
    [Fact]
    public void Redacts_JwtToken()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhYmMifQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var result = SecretRedactor.Redact($"Token: {jwt} end");
        Assert.Contains("[REDACTED:jwt]", result);
        Assert.DoesNotContain(jwt, result);
    }

    [Fact]
    public void Redacts_PasswordKeyValue_InConnectionString()
    {
        const string conn = "Server=db;User ID=admin;Password=Hunter2;Database=Sf";
        var result = SecretRedactor.Redact(conn);
        Assert.Contains("Password=[REDACTED]", result);
        Assert.DoesNotContain("Hunter2", result);
    }

    [Fact]
    public void Redacts_AwsAccessKey()
    {
        var result = SecretRedactor.Redact("key is AKIAIOSFODNN7EXAMPLE here");
        Assert.Contains("[REDACTED:aws-key]", result);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", result);
    }

    [Fact]
    public void Redacts_GitHubPat()
    {
        const string pat = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        var result = SecretRedactor.Redact($"token: {pat}");
        Assert.Contains("[REDACTED:github]", result);
        Assert.DoesNotContain(pat, result);
    }

    [Fact]
    public void Redacts_BearerToken_InHeader()
    {
        var result = SecretRedactor.Redact("Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789");
        Assert.Contains("[REDACTED:bearer]", result);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz0123456789", result);
    }

    [Fact]
    public void Redacts_OpenAIKey()
    {
        var result = SecretRedactor.Redact("sk-proj-abcdefghijklmno1234567890ABCDEF");
        Assert.Contains("[REDACTED:openai]", result);
    }

    [Fact]
    public void Redacts_AzureStorageAccountKey()
    {
        var result = SecretRedactor.Redact("AccountKey=yDQeBsI6HPwZ+5gh2oihuGy14Ghe+abcdEFGHijkl==");
        Assert.Contains("AccountKey=[REDACTED:azure-storage]", result);
    }

    [Fact]
    public void RedactsDictionary_ByKeyName_PasswordApiKeySecret()
    {
        var props = new Dictionary<string, string>
        {
            ["Title"] = "About Us",
            ["Password"] = "Hunter2",
            ["apiKey"] = "abc-123",
            ["ClientSecret"] = "shh",
            ["SmtpPassword"] = "mail-secret",
            ["Content"] = "<p>Nothing secret here</p>",
        };

        var result = SecretRedactor.RedactDictionary(props);

        Assert.Equal("About Us", result["Title"]);
        Assert.Equal(SecretRedactor.Placeholder, result["Password"]);
        Assert.Equal(SecretRedactor.Placeholder, result["apiKey"]);
        Assert.Equal(SecretRedactor.Placeholder, result["ClientSecret"]);
        Assert.Equal(SecretRedactor.Placeholder, result["SmtpPassword"]);
        Assert.Contains("Nothing secret", result["Content"]);
    }

    [Fact]
    public void PreservesNonSensitiveContent()
    {
        const string html = "<p>Hello world, this is an article about <a href='/blog/post'>Sitefinity</a>.</p>";
        var result = SecretRedactor.Redact(html);
        Assert.Equal(html, result);
    }

    [Fact]
    public void HandlesEmptyAndNull()
    {
        Assert.Equal(string.Empty, SecretRedactor.Redact(null));
        Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
    }

    [Theory]
    [InlineData("Password", true)]
    [InlineData("password", true)]
    [InlineData("ApiKey", true)]
    [InlineData("X-API-Key", true)]
    [InlineData("ClientSecret", true)]
    [InlineData("MySmtpPassword", true)] // contains "password"
    [InlineData("Title", false)]
    [InlineData("Content", false)]
    [InlineData("UrlName", false)]
    public void IsDeniedKey_ClassifiesCorrectly(string key, bool expected)
    {
        Assert.Equal(expected, SecretRedactor.IsDeniedKey(key));
    }

    [Fact]
    public void Redact_AppliesMultiplePatterns_InSingleString()
    {
        const string payload =
            "SqlException: Server=x;Password=Hunter2;" +
            " + header Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789" +
            " + token eyJabcdefghij.def123456789xyz.ghi987654321ok";
        var result = SecretRedactor.Redact(payload);
        Assert.Contains("Password=[REDACTED]", result);
        Assert.Contains("[REDACTED:bearer]", result);
        Assert.Contains("[REDACTED:jwt]", result);
        Assert.DoesNotContain("Hunter2", result);
    }
}

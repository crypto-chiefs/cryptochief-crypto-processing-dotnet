using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CryptoChief.Processing;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class UserAgentTests
{
    /// <summary>Package version: major.minor.patch with an optional pre-release tag, no build metadata.</summary>
    private const string VersionPattern = @"^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$";

    [Fact]
    public void Version_is_the_package_version()
    {
        CryptoChiefClient.Version.Should().MatchRegex(VersionPattern);
        CryptoChiefClient.Version.Should().Be(PackageVersionFromProject());
    }

    [Fact]
    public void Default_user_agent_names_the_package_version()
    {
        new CryptoChiefClientOptions().UserAgent
            .Should().Be($"cryptochief-dotnet/{CryptoChiefClient.Version}")
            .And.MatchRegex(@"^cryptochief-dotnet/[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$");
    }

    [Fact]
    public async Task Request_carries_the_default_user_agent()
    {
        var handler = new CapturingHandler(_ => Resp(HttpStatusCode.OK,
            "{\"uuid\":\"u-1\",\"order_id\":\"o-1\",\"status\":\"queue\"}"));
        var client = NewClient(handler);

        await client.Payouts.ExecuteAsync(new ExecutePayoutRequest
        {
            OrderId     = "o-1",
            UserId      = "u-7",
            Network     = "ETH_SEPOLIA",
            Coin        = "ETH",
            Amount      = "0.0001",
            ToAddress   = "0xRecipient",
            UrlCallback = "https://app/cb",
        });

        var req = handler.Captured.Should().ContainSingle().Subject;
        var ua = req.Headers.GetValues("User-Agent").Should().ContainSingle().Subject;
        ua.Should().Be($"cryptochief-dotnet/{CryptoChiefClient.Version}");
        ua.Should().NotContain("+");
    }

    /// <summary>&lt;Version&gt; from the library project — the version NuGet publishes.</summary>
    private static string PackageVersionFromProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CryptoChief.Processing.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test runs inside the repository");

        var csproj = Path.Combine(dir!.FullName, "src", "CryptoChief.Processing", "CryptoChief.Processing.csproj");
        var match = Regex.Match(File.ReadAllText(csproj), @"<Version>([^<]+)</Version>");
        match.Success.Should().BeTrue("the project declares <Version>");
        return match.Groups[1].Value;
    }

    private static HttpResponseMessage Resp(HttpStatusCode code, string body) =>
        new(code) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") } } };

    private static CryptoChiefClient NewClient(HttpMessageHandler handler) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId        = "M-1",
            ApiKey            = "K-1",
            BaseUrl           = "https://test/",
            MaxRetries        = 0,
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay     = TimeSpan.FromMilliseconds(5),
        }, new HttpClient(handler), null);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Captured { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            return Task.FromResult(reply(request));
        }
    }
}

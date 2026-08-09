using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using AICopilot.Infrastructure.CloudIdentity;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.InProcessTests;

public sealed class CloudIdentityStatusTokenProviderTests
{
    private const string SigningSecret =
        "identity-status-signing-secret-with-more-than-thirty-two-bytes";

    [Fact]
    public async Task GetTokenAsync_ShouldUseOnlyFixedIdentityStatusClaimsAndFiveMinuteLifetime()
    {
        var now = new DateTimeOffset(2026, 8, 9, 1, 2, 3, TimeSpan.Zero);
        var provider = CreateProvider(new ManualTimeProvider(now));

        var token = await provider.GetTokenAsync();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Issuer.Should().Be(CloudIdentityStatusTokenDefaults.Issuer);
        jwt.Audiences.Should().Equal(CloudIdentityStatusTokenDefaults.Audience);
        jwt.Subject.Should().Be(CloudIdentityStatusTokenDefaults.Subject);
        jwt.Claims.Single(claim =>
                claim.Type == CloudIdentityStatusTokenDefaults.ActorClaimType)
            .Value.Should().Be(CloudIdentityStatusTokenDefaults.Actor);
        jwt.Claims.Should().NotContain(claim =>
            claim.Type.Contains("role", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Contains("scope", StringComparison.OrdinalIgnoreCase));
        (jwt.ValidTo - jwt.ValidFrom).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Options_ShouldMeasureSigningSecretInUtf8Bytes()
    {
        var options = new CloudIdentityStatusOptions
        {
            Enabled = true,
            BaseUrl = "https://cloud.example.com",
            SigningSecret = new string('\u754c', 11)
        };

        Action act = options.EnsureValid;

        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetTokenAsync_ShouldReuseCacheThenRenewWithSixtySecondsRemaining()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 9, 1, 0, 0, TimeSpan.Zero));
        var provider = CreateProvider(clock);

        var first = await provider.GetTokenAsync();
        clock.Advance(TimeSpan.FromMinutes(3));
        (await provider.GetTokenAsync()).Should().Be(first);

        clock.Advance(TimeSpan.FromMinutes(1));
        var renewed = await provider.GetTokenAsync();
        renewed.Should().NotBe(first);
        new JwtSecurityTokenHandler().ReadJwtToken(renewed).ValidTo
            .Should().BeAfter(new JwtSecurityTokenHandler().ReadJwtToken(first).ValidTo);

        provider.Invalidate();
        (await provider.GetTokenAsync()).Should().NotBe(renewed);
    }

    [Fact]
    public async Task Client_ShouldInvalidateAndRetryExactlyOnceAfterUnauthorized()
    {
        var cloudUserId = Guid.NewGuid().ToString("D");
        var handler = new SequencedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {"cloudUserId":"{{cloudUserId}}","tenantId":"default","accountEnabled":true,"employeeActive":true,"statusVersion":"v1","issuedAtUtc":"2026-08-09T01:00:00Z"}
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        var tokenProvider = new RecordingTokenProvider();
        var client = CreateClient(handler, tokenProvider);

        var result = await client.GetStatusAsync(cloudUserId, "default");

        result.Outcome.Should().Be(CloudIdentityStatusCheckOutcome.Succeeded);
        handler.CallCount.Should().Be(2);
        tokenProvider.GetCalls.Should().Be(2);
        tokenProvider.InvalidateCalls.Should().Be(1);
        handler.AuthorizationValues.Should().Equal("token-1", "token-2");
    }

    [Fact]
    public async Task Client_ShouldFailClosedAfterSecondUnauthorized()
    {
        var handler = new SequencedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var tokenProvider = new RecordingTokenProvider();
        var client = CreateClient(handler, tokenProvider);

        var result = await client.GetStatusAsync(Guid.NewGuid().ToString("D"), "default");

        result.Outcome.Should().Be(CloudIdentityStatusCheckOutcome.Unavailable);
        handler.CallCount.Should().Be(2);
        tokenProvider.GetCalls.Should().Be(2);
        tokenProvider.InvalidateCalls.Should().Be(1);
    }

    private static CloudIdentityStatusTokenProvider CreateProvider(TimeProvider timeProvider) =>
        new(
            Options.Create(new CloudIdentityStatusOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.com",
                SigningSecret = SigningSecret
            }),
            timeProvider);

    private static CloudIdentityStatusClient CreateClient(
        HttpMessageHandler handler,
        ICloudIdentityStatusTokenProvider tokenProvider) =>
        new(
            new HttpClient(handler),
            Options.Create(new CloudIdentityStatusOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.com",
                SigningSecret = SigningSecret
            }),
            tokenProvider);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }

    private sealed class RecordingTokenProvider : ICloudIdentityStatusTokenProvider
    {
        public int GetCalls { get; private set; }

        public int InvalidateCalls { get; private set; }

        public ValueTask<string> GetTokenAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetCalls++;
            return ValueTask.FromResult($"token-{GetCalls}");
        }

        public void Invalidate() => InvalidateCalls++;
    }

    private sealed class SequencedHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public List<string> AuthorizationValues { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorizationValues.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            var index = Math.Min(CallCount, responses.Length - 1);
            CallCount++;
            return Task.FromResult(responses[index](request));
        }
    }
}

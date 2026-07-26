using AICopilot.HttpApi.Infrastructure;

namespace AICopilot.InProcessTests;

public sealed class SystemBuildIdentityResolverTests
{
    private const string SourceCommit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void Resolve_ShouldExposeOnlyMatchingImmutableBuildIdentity()
    {
        var result = SystemBuildIdentityResolver.Resolve(
            SourceCommit.ToUpperInvariant(),
            $"SHA-{SourceCommit.ToUpperInvariant()}");

        result.SchemaVersion.Should().Be(SystemBuildIdentityResolver.SchemaVersion);
        result.ServiceName.Should().Be(SystemBuildIdentityResolver.ServiceName);
        result.ReleaseTag.Should().Be($"sha-{SourceCommit}");
        result.SourceCommit.Should().Be(SourceCommit);
        result.Available.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("0123456", "sha-0123456")]
    [InlineData(SourceCommit, "sha-ffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("not-a-git-commit", "sha-not-a-git-commit")]
    public void Resolve_ShouldFailClosedWithoutCrossFieldFallback(
        string? sourceCommit,
        string? releaseTag)
    {
        var result = SystemBuildIdentityResolver.Resolve(sourceCommit, releaseTag);

        result.Available.Should().BeFalse();
        result.SourceCommit.Should().BeNull();
        result.ReleaseTag.Should().BeNull();
    }
}

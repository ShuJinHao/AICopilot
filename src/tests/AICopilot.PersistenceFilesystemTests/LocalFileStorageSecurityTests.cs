using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Infrastructure.Storage;
using AICopilot.PersistenceTestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.PersistenceFilesystemTests;

public sealed class LocalFileStorageSecurityTests
{
    [Fact]
    public async Task LocalFileStorageService_ShouldConstrainAccessToConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aicopilot-storage-tests", Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = root
            })
            .Build();

        try
        {
            var storage = new LocalFileStorageService(configuration);
            var commitScope = new PersistenceCommitScope();
            var persistentStorage = new LocalPersistenceFileStorageService(
                storage,
                storage,
                new AlwaysAcquiredPersistenceFileLeaseManager(),
                commitScope,
                NullLogger<LocalPersistenceFileStorageService>.Instance);
            await using var payload = new MemoryStream([1, 2, 3]);

            var stage = await persistentStorage.StageAsync(payload, "../unsafe.txt");
            var relativePath = stage.StoragePath;

            relativePath.Should().StartWith("uploads/");
            relativePath.Should().NotContain("..");
            relativePath.Should().EndWith("unsafe.txt");

            var stored = await storage.GetAsync(relativePath);
            stored.Should().NotBeNull();
            await using (stored!)
            {
                using var roundTrip = new MemoryStream();
                await stored.CopyToAsync(roundTrip);
                roundTrip.ToArray().Should().Equal(1, 2, 3);
            }

            commitScope.ReleaseCommitId(stage.CommitId);
            await persistentStorage.ConfirmBestEffortAsync(stage);
            await storage.DeleteAsync(relativePath);
            (await storage.GetAsync(relativePath)).Should().BeNull();

            Func<Task> traversalGet = async () => await storage.GetAsync("../escape.txt");
            Func<Task> nestedTraversalDelete = () => storage.DeleteAsync("uploads/../../escape.txt");
            Func<Task> absoluteGet = async () => await storage.GetAsync(
                Path.GetFullPath(Path.Combine(root, "..", "escape.txt")));

            await traversalGet.Should().ThrowAsync<InvalidOperationException>();
            await nestedTraversalDelete.Should().ThrowAsync<InvalidOperationException>();
            await absoluteGet.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

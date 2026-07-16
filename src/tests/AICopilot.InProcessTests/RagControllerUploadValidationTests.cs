using AICopilot.HttpApi.Controllers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AICopilot.InProcessTests;

public sealed class RagControllerUploadValidationTests
{
    [Fact]
    public async Task UploadDocument_ShouldRejectMissingEmptyOrTooLargeFilesBeforeDispatch()
    {
        var controller = new RagController(new ThrowingSender());

        var missingResult = await controller.UploadDocument(Guid.NewGuid(), null);
        var emptyResult = await controller.UploadDocument(
            Guid.NewGuid(),
            new FormFile(new MemoryStream(), 0, 0, "file", "empty.txt"));
        var largeResult = await controller.UploadDocument(
            Guid.NewGuid(),
            new FormFile(
                new MemoryStream([1]),
                0,
                RagController.MaxDocumentUploadBytes + 1,
                "file",
                "large.txt"));

        missingResult.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { error = "File is required." });
        emptyResult.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { error = "File is required." });
        largeResult.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { error = "File exceeds the 50 MB upload limit." });
    }

    private sealed class ThrowingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }
    }
}

using IndustrialCopilot.Application.Abstractions.Documents.Models;

namespace IndustrialCopilot.Application.Tests.Abstractions.Documents;

public class DocumentContractTests
{
    [Fact]
    public void RejectsEmptyIdentities()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new DocumentProcessingRequest(Guid.Empty, id, "application/pdf"));
        Assert.Throws<ArgumentException>(() => new DocumentProcessingRequest(id, Guid.Empty, "application/pdf"));
        Assert.Throws<ArgumentException>(() => new DocumentChunk(Guid.Empty, id, id, "Page 1", "Text"));
        Assert.Throws<ArgumentException>(() => new DocumentChunk(id, Guid.Empty, id, "Page 1", "Text"));
        Assert.Throws<ArgumentException>(() => new DocumentChunk(id, id, Guid.Empty, "Page 1", "Text"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingMediaTypeLocatorOrContent(string? value)
    {
        var id = Guid.NewGuid();
        Assert.ThrowsAny<ArgumentException>(() => new DocumentProcessingRequest(id, id, value!));
        Assert.ThrowsAny<ArgumentException>(() => new DocumentChunk(id, id, id, value!, "Text"));
        Assert.ThrowsAny<ArgumentException>(() => new DocumentChunk(id, id, id, "Page 1", value!));
    }
}

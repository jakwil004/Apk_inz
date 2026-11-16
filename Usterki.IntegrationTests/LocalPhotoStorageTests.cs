using Usterki.Infrastructure.Storage;
using Xunit;

namespace Usterki.IntegrationTests;

public sealed class LocalPhotoStorageTests : IDisposable
{
    private readonly string _directory;
    private readonly LocalPhotoStorage _storage;

    public LocalPhotoStorageTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "UsterkiStorageTests",
            Guid.NewGuid().ToString("N"));

        _storage = new LocalPhotoStorage(_directory);
    }

    [Fact]
    public async Task SaveAsync_CanReadExactlyTheSavedContent()
    {
        byte[] expected = [10, 20, 30, 40, 50];

        var storageId = await _storage.SaveAsync(expected);

        Assert.NotEqual(Guid.Empty, storageId);

        await using var stream =
            await _storage.OpenReadAsync(storageId);

        Assert.NotNull(stream);

        using var buffer = new MemoryStream();

        await stream!.CopyToAsync(buffer);

        Assert.Equal(expected, buffer.ToArray());

        Assert.Empty(
            Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndCanBeRepeated()
    {
        byte[] content = [1, 2, 3];

        var storageId = await _storage.SaveAsync(content);

        await _storage.DeleteAsync(storageId);
        await _storage.DeleteAsync(storageId);

        var stream = await _storage.OpenReadAsync(storageId);

        Assert.Null(stream);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveAsync_RejectsEmptyContent()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _storage.SaveAsync(
                ReadOnlyMemory<byte>.Empty));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveAsync_RejectsOversizedContent()
    {
        var content = new byte[
            LocalPhotoStorage.MaxStoredFileSizeBytes + 1];

        await Assert.ThrowsAsync<ArgumentException>(
            () => _storage.SaveAsync(content));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveAsync_CancelledOperation_DoesNotCreateFile()
    {
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _storage.SaveAsync(
                new byte[] { 1, 2, 3 },
                cancellation.Token));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
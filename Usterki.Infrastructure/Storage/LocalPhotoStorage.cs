using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Infrastructure.Storage;

public sealed class LocalPhotoStorage : IPhotoStorage
{
    public const int MaxStoredFileSizeBytes = 10 * 1024 * 1024;

    private readonly string _rootPath;

    public LocalPhotoStorage(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (!Path.IsPathFullyQualified(rootPath))
        {
            throw new ArgumentException(
                "Katalog zdjęć musi mieć ścieżkę bezwzględną.",
                nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<Guid> SaveAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            throw new ArgumentException(
                "Plik nie może być pusty.",
                nameof(content));
        }

        if (content.Length > MaxStoredFileSizeBytes)
        {
            throw new ArgumentException(
                "Plik przekracza limit 10 MiB.",
                nameof(content));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var storageId = Guid.NewGuid();

        var finalPath = GetFilePath(storageId);
        var temporaryPath = Path.Combine(
            _rootPath,
            $"{storageId:N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous))
            {
                await stream.WriteAsync(
                    content,
                    cancellationToken);

                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(
                temporaryPath,
                finalPath,
                overwrite: false);

            return storageId;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(
        Guid storageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetFilePath(storageId);

        try
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                options:
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);

            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(
        Guid storageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        File.Delete(GetFilePath(storageId));

        return Task.CompletedTask;
    }

    private string GetFilePath(Guid storageId)
    {
        if (storageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Identyfikator pliku nie może być pusty.",
                nameof(storageId));
        }

        return Path.Combine(
            _rootPath,
            $"{storageId:N}.bin");
    }
}
using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Infrastructure.Storage;

public interface IPhotoStorage
{
    Task<Guid> SaveAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        Guid storageId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid storageId,
        CancellationToken cancellationToken = default);
}
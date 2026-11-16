using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Contracts.Tickets;

public sealed record TicketPhotoResponse(
    Guid Id,
    Guid TicketId,
    Guid UploadedByUserId,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height,
    DateTime CreatedAtUtc);
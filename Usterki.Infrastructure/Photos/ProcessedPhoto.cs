using System;
using System.Collections.Generic;
using System.Text;
namespace Usterki.Infrastructure.Photos;

public sealed record ProcessedPhoto(
    byte[] Content,
    string ContentType,
    int Width,
    int Height);
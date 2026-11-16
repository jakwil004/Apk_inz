using System;
using System.Collections.Generic;
using System.Text;
namespace Usterki.Infrastructure.Photos;

public sealed class PhotoValidationException : Exception
{
    public PhotoValidationException(string message) : base(message)
    {
    }
}
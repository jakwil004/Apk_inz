using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Usterki.Api.Models;

public sealed class UploadTicketPhotoRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
}
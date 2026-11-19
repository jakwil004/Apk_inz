using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace Usterki.Contracts.Notifications;

public sealed class RegisterPushDeviceRequest
{
    [Required]
    [StringLength(200, MinimumLength = 10)]
    public string FirebaseInstallationId { get; set; } = string.Empty;
}
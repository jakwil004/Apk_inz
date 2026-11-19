using System;
using System.Collections.Generic;
using System.Text;

namespace Usterki.Infrastructure.Notifications;

public enum PushSendResult
{
    Accepted,
    Unregistered,
    Retry
}

public interface IPushSender
{
    Task<PushSendResult> SendAsync(
        string firebaseInstallationId,
        Guid notificationId,
        CancellationToken cancellationToken);
}
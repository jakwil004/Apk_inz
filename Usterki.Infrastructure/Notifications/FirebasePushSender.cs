using System;
using System.Collections.Generic;
using System.Text;
using FirebaseAdmin.Messaging;

namespace Usterki.Infrastructure.Notifications;

public sealed class FirebasePushSender : IPushSender
{
    private readonly FirebaseMessaging _messaging;

    public FirebasePushSender(FirebaseMessaging messaging)
    {
        _messaging = messaging;
    }

    public async Task<PushSendResult> SendAsync(
        string firebaseInstallationId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var message = new Message
        {
            Fid = firebaseInstallationId,

            Notification = new Notification
            {
                Title = "Usterki",
                Body = "Masz nowe powiadomienie w aplikacji."
            },

            Data = new Dictionary<string, string>
            {
                ["notificationId"] = notificationId.ToString()
            },

            Android = new AndroidConfig
            {
                Notification = new AndroidNotification
                {
                    Tag = notificationId.ToString()
                }
            }
        };

        try
        {
            await _messaging.SendAsync(
                message,
                cancellationToken);

            return PushSendResult.Accepted;
        }
        catch (FirebaseMessagingException exception)
            when (exception.MessagingErrorCode ==
                  MessagingErrorCode.Unregistered)
        {
            return PushSendResult.Unregistered;
        }
        catch (FirebaseMessagingException)
        {
            return PushSendResult.Retry;
        }
    }
}
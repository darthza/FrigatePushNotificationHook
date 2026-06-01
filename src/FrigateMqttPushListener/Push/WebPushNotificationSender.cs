using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FrigateMqttPushListener.Events;
using FrigateMqttPushListener.Options;
using Microsoft.Extensions.Options;
using WebPush;

namespace FrigateMqttPushListener.Push;

public sealed class WebPushNotificationSender
{
    private readonly FrigateSubscriptionStore _subscriptions;
    private readonly IOptionsMonitor<ListenerOptions> _listenerOptions;
    private readonly IOptionsMonitor<PushOptions> _pushOptions;
    private readonly ILogger<WebPushNotificationSender> _logger;

    public WebPushNotificationSender(
        FrigateSubscriptionStore subscriptions,
        IOptionsMonitor<ListenerOptions> listenerOptions,
        IOptionsMonitor<PushOptions> pushOptions,
        ILogger<WebPushNotificationSender> logger)
    {
        _subscriptions = subscriptions;
        _listenerOptions = listenerOptions;
        _pushOptions = pushOptions;
        _logger = logger;
    }

    public async Task SendAsync(FrigateEvent frigateEvent, CancellationToken cancellationToken)
    {
        var listenerOptions = _listenerOptions.CurrentValue;
        var title = Render(listenerOptions.TitleTemplate, frigateEvent);
        var body = Render(listenerOptions.BodyTemplate, frigateEvent);

        if (listenerOptions.DryRun)
        {
            _logger.LogInformation("DRY RUN push: {Title} - {Body}", title, body);
            return;
        }

        var subscriptions = await _subscriptions.GetSubscriptionsAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            _logger.LogWarning("No Frigate push subscriptions found");
            return;
        }

        var vapid = GetVapidDetails();
        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            tag = $"frigate-{frigateEvent.Camera}-{frigateEvent.Id}",
            data = new
            {
                eventId = frigateEvent.Id,
                camera = frigateEvent.Camera,
                label = frigateEvent.Label,
                score = frigateEvent.Score,
                type = frigateEvent.Type
            }
        });

        using var client = new WebPushClient();
        foreach (var subscription in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256Dh, subscription.Auth);

            try
            {
                await client.SendNotificationAsync(pushSubscription, payload, vapid);
                _logger.LogInformation("Sent push to subscription ending in {EndpointSuffix}", Suffix(subscription.Endpoint));
            }
            catch (WebPushException ex) when ((int?)ex.StatusCode is 400 or 404 or 410)
            {
                _logger.LogWarning(
                    "Stale push subscription returned {StatusCode}: {Message}",
                    ex.StatusCode,
                    ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification");
            }
        }
    }

    private VapidDetails GetVapidDetails()
    {
        var options = _pushOptions.CurrentValue;
        if (!string.IsNullOrWhiteSpace(options.VapidPublicKey) &&
            !string.IsNullOrWhiteSpace(options.VapidPrivateKey))
        {
            return new VapidDetails(options.Subject, options.VapidPublicKey, options.VapidPrivateKey);
        }

        var (publicKey, privateKey) = ReadVapidKeysFromPem(options.NotificationsPemPath);
        return new VapidDetails(options.Subject, publicKey, privateKey);
    }

    private static (string PublicKey, string PrivateKey) ReadVapidKeysFromPem(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("VAPID PEM file was not found", path);
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(path));
        var parameters = ecdsa.ExportParameters(true);

        if (parameters.Q.X is null || parameters.Q.Y is null || parameters.D is null)
        {
            throw new InvalidOperationException("Could not export VAPID key material from PEM file");
        }

        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        Buffer.BlockCopy(parameters.Q.X, 0, publicKey, 1, 32);
        Buffer.BlockCopy(parameters.Q.Y, 0, publicKey, 33, 32);

        return (Base64UrlEncode(publicKey), Base64UrlEncode(parameters.D));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Render(string template, FrigateEvent frigateEvent)
    {
        var person = !string.IsNullOrWhiteSpace(frigateEvent.SubLabel)
            ? frigateEvent.SubLabel
            : frigateEvent.Label;

        var subLabelDisplay = !string.IsNullOrWhiteSpace(frigateEvent.SubLabel)
            ? frigateEvent.SubLabel
            : "unknown";

        return template
            .Replace("{camera}", frigateEvent.Camera, StringComparison.OrdinalIgnoreCase)
            .Replace("{label}", frigateEvent.Label, StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", frigateEvent.Type, StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", frigateEvent.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{sub_label}", subLabelDisplay, StringComparison.OrdinalIgnoreCase)
            .Replace("{person}", person, StringComparison.OrdinalIgnoreCase)
            .Replace("{score}", FormatScore(frigateEvent.Score), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatScore(double? score)
    {
        return score is null ? "unknown" : score.Value.ToString("P0", CultureInfo.InvariantCulture);
    }

    private static string Suffix(string value)
    {
        return value.Length <= 8 ? value : value[^8..];
    }
}

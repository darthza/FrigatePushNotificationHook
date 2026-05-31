namespace FrigateMqttPushListener.Options;

public sealed class PushOptions
{
    public string FrigateDatabasePath { get; set; } = "/frigate/frigate.db";
    public string NotificationsPemPath { get; set; } = "/frigate/notifications.pem";
    public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKey { get; set; }
    public string Subject { get; set; } = "mailto:admin@example.com";
    public SubscriptionQueryOptions Subscriptions { get; set; } = new();
}

public sealed class SubscriptionQueryOptions
{
    public string? Table { get; set; }
    public string EndpointColumn { get; set; } = "endpoint";
    public string P256DhColumn { get; set; } = "p256dh";
    public string AuthColumn { get; set; } = "auth";
}

namespace FrigateMqttPushListener.Push;

public sealed record FrigatePushSubscription(string Endpoint, string P256Dh, string Auth);

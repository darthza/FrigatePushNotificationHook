namespace FrigateMqttPushListener.Options;

public sealed class MqttOptions
{
    public string Host { get; set; } = "mosquitto";
    public int Port { get; set; } = 1883;
    public string Topic { get; set; } = "frigate/events";
    public string ClientId { get; set; } = "frigate-mqtt-push-listener";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int ReconnectDelaySeconds { get; set; } = 5;
}

using System.Text;
using FrigateMqttPushListener.Events;
using FrigateMqttPushListener.Filtering;
using FrigateMqttPushListener.Options;
using FrigateMqttPushListener.Push;
using FrigateMqttPushListener.State;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace FrigateMqttPushListener.Mqtt;

public sealed class FrigateMqttWorker : BackgroundService
{
    private readonly IOptionsMonitor<MqttOptions> _mqttOptions;
    private readonly NotificationFilter _filter;
    private readonly StateStore _stateStore;
    private readonly WebPushNotificationSender _pushSender;
    private readonly ILogger<FrigateMqttWorker> _logger;

    public FrigateMqttWorker(
        IOptionsMonitor<MqttOptions> mqttOptions,
        NotificationFilter filter,
        StateStore stateStore,
        WebPushNotificationSender pushSender,
        ILogger<FrigateMqttWorker> logger)
    {
        _mqttOptions = mqttOptions;
        _filter = filter;
        _stateStore = stateStore;
        _pushSender = pushSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttFactory();

        while (!stoppingToken.IsCancellationRequested)
        {
            using var client = factory.CreateMqttClient();
            client.ApplicationMessageReceivedAsync += HandleMessageAsync;

            try
            {
                var options = _mqttOptions.CurrentValue;
                var clientOptionsBuilder = new MqttClientOptionsBuilder()
                    .WithClientId(options.ClientId)
                    .WithTcpServer(options.Host, options.Port)
                    .WithCleanSession();

                if (!string.IsNullOrWhiteSpace(options.Username))
                {
                    clientOptionsBuilder.WithCredentials(options.Username, options.Password);
                }

                _logger.LogInformation("Connecting to MQTT broker {Host}:{Port}", options.Host, options.Port);
                await client.ConnectAsync(clientOptionsBuilder.Build(), stoppingToken);

                var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(filter => filter.WithTopic(options.Topic))
                    .Build();

                await client.SubscribeAsync(subscribeOptions, stoppingToken);
                _logger.LogInformation("Subscribed to MQTT topic {Topic}", options.Topic);

                while (!stoppingToken.IsCancellationRequested && client.IsConnected)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT listener loop failed");
            }

            var delay = TimeSpan.FromSeconds(Math.Max(1, _mqttOptions.CurrentValue.ReconnectDelaySeconds));
            _logger.LogInformation("Reconnecting to MQTT in {DelaySeconds} seconds", delay.TotalSeconds);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

        FrigateEvent? frigateEvent;
        try
        {
            if (!FrigateEventParser.TryParse(payload, out frigateEvent) || frigateEvent is null)
            {
                _logger.LogDebug("Ignoring MQTT payload that does not look like a Frigate event");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Frigate MQTT payload");
            return;
        }

        if (!_filter.ShouldNotify(frigateEvent, out var reason))
        {
            _logger.LogInformation(
                "Skipping Frigate event {EventId} from {Camera}: {Reason}",
                frigateEvent.Id,
                frigateEvent.Camera,
                reason);
            return;
        }

        var reserveResult = await _stateStore.TryReserveNotificationAsync(frigateEvent, CancellationToken.None);
        if (!reserveResult.Reserved)
        {
            _logger.LogInformation(
                "Skipping Frigate event {EventId} from {Camera}: {Reason}",
                frigateEvent.Id,
                frigateEvent.Camera,
                reserveResult.Reason);
            return;
        }

        _logger.LogInformation(
            "Notifying for Frigate event {EventId}: {Label} on {Camera}",
            frigateEvent.Id,
            frigateEvent.Label,
            frigateEvent.Camera);

        await _pushSender.SendAsync(frigateEvent, CancellationToken.None);
    }
}

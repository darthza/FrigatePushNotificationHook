using Microsoft.Data.Sqlite;
using System.Text.Json;
using FrigateMqttPushListener.Options;
using Microsoft.Extensions.Options;

namespace FrigateMqttPushListener.Push;

public sealed class FrigateSubscriptionStore
{
    private readonly IOptionsMonitor<PushOptions> _options;
    private readonly ILogger<FrigateSubscriptionStore> _logger;

    public FrigateSubscriptionStore(IOptionsMonitor<PushOptions> options, ILogger<FrigateSubscriptionStore> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FrigatePushSubscription>> GetSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!File.Exists(options.FrigateDatabasePath))
        {
            _logger.LogWarning("Frigate database not found at {Path}", options.FrigateDatabasePath);
            return [];
        }

        var subscriptions = new List<FrigatePushSubscription>();
        try
        {
            // Use Mode=ReadOnly to support reading WAL database from read-only mount
            await using var connection = new SqliteConnection($"Data Source={options.FrigateDatabasePath};Mode=ReadOnly");
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "select notification_tokens from user";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var jsonStr = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(jsonStr))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (!element.TryGetProperty("endpoint", out var endpointProp) || endpointProp.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var endpoint = endpointProp.GetString();
                        if (string.IsNullOrWhiteSpace(endpoint))
                        {
                            continue;
                        }

                        string? p256dh = null;
                        string? auth = null;

                        if (element.TryGetProperty("keys", out var keysProp) && keysProp.ValueKind == JsonValueKind.Object)
                        {
                            if (keysProp.TryGetProperty("p256dh", out var p256dhProp) && p256dhProp.ValueKind == JsonValueKind.String)
                            {
                                p256dh = p256dhProp.GetString();
                            }
                            if (keysProp.TryGetProperty("auth", out var authProp) && authProp.ValueKind == JsonValueKind.String)
                            {
                                auth = authProp.GetString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(p256dh) && !string.IsNullOrWhiteSpace(auth))
                        {
                            subscriptions.Add(new FrigatePushSubscription(endpoint, p256dh, auth));
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse notification_tokens JSON string");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load push subscriptions from Frigate database");
        }

        return subscriptions;
    }
}

using System.Text.Json;
using FrigateMqttPushListener.Events;
using FrigateMqttPushListener.Options;
using Microsoft.Extensions.Options;

namespace FrigateMqttPushListener.State;

public sealed class StateStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IOptionsMonitor<StateOptions> _stateOptions;
    private readonly IOptionsMonitor<ListenerOptions> _listenerOptions;
    private readonly ILogger<StateStore> _logger;
    private ListenerState? _state;

    public StateStore(
        IOptionsMonitor<StateOptions> stateOptions,
        IOptionsMonitor<ListenerOptions> listenerOptions,
        ILogger<StateStore> logger)
    {
        _stateOptions = stateOptions;
        _listenerOptions = listenerOptions;
        _logger = logger;
    }

    public async Task<bool> TryReserveNotificationAsync(FrigateEvent frigateEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var listenerOptions = _listenerOptions.CurrentValue;
            var cooldown = TimeSpan.FromSeconds(Math.Max(0, listenerOptions.CooldownSeconds));

            var alreadyNotified = state.NotifiedEvents.TryGetValue(frigateEvent.Id, out var notifiedSubLabel);

            if (alreadyNotified)
            {
                // If we already notified for this event, we only send an update if NotifyOnSubLabelChange is enabled,
                // the new event has a sub_label, and we previously notified with an empty sub_label.
                if (listenerOptions.NotifyOnSubLabelChange &&
                    !string.IsNullOrWhiteSpace(frigateEvent.SubLabel) &&
                    string.IsNullOrWhiteSpace(notifiedSubLabel))
                {
                    state.NotifiedEvents[frigateEvent.Id] = frigateEvent.SubLabel;
                    await SaveAsync(state, cancellationToken);
                    return true;
                }

                return false;
            }

            if (state.LastNotificationByCamera.TryGetValue(frigateEvent.Camera, out var lastNotification) &&
                now - lastNotification < cooldown)
            {
                return false;
            }

            state.NotifiedEvents[frigateEvent.Id] = frigateEvent.SubLabel;
            state.LastNotificationByCamera[frigateEvent.Camera] = now;

            while (state.NotifiedEvents.Count > 500)
            {
                var oldestKey = state.NotifiedEvents.Keys.FirstOrDefault();
                if (oldestKey is not null)
                {
                    state.NotifiedEvents.Remove(oldestKey);
                }
            }

            await SaveAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ListenerState> LoadAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            return _state;
        }

        var path = _stateOptions.CurrentValue.Path;
        if (!File.Exists(path))
        {
            _state = new ListenerState();
            return _state;
        }

        await using var stream = File.OpenRead(path);
        _state = await JsonSerializer.DeserializeAsync<ListenerState>(stream, cancellationToken: cancellationToken) ?? new ListenerState();

        _state.LastNotificationByCamera ??= new(StringComparer.OrdinalIgnoreCase);
        _state.NotifiedEvents ??= new(StringComparer.OrdinalIgnoreCase);

        // Migrate old state NotifiedEventIds if they exist
        if (_state.NotifiedEventIds is { Count: > 0 } oldIds)
        {
            foreach (var id in oldIds)
            {
                _state.NotifiedEvents[id] = null;
            }
            _state.NotifiedEventIds.Clear();
        }

        return _state;
    }

    private async Task SaveAsync(ListenerState state, CancellationToken cancellationToken)
    {
        var path = _stateOptions.CurrentValue.Path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
        _logger.LogDebug("Saved listener state to {Path}", path);
    }

    private sealed class ListenerState
    {
        public Dictionary<string, DateTimeOffset> LastNotificationByCamera { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> NotifiedEvents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> NotifiedEventIds { get; set; } = [];
    }
}

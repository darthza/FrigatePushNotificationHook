using System.Text.Json;

namespace FrigateMqttPushListener.Events;

public sealed record FrigateEvent(
    string Id,
    string Camera,
    string Label,
    string Type,
    IReadOnlyList<string> EnteredZones,
    string? SubLabel,
    JsonElement Raw);

public static class FrigateEventParser
{
    public static bool TryParse(string payload, out FrigateEvent? frigateEvent)
    {
        frigateEvent = null;

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement.Clone();

        var eventType = ReadString(root, "type") ?? "unknown";
        var data = root.TryGetProperty("after", out var after) ? after : root;

        var id = ReadString(data, "id") ?? ReadString(root, "id");
        var camera = ReadString(data, "camera") ?? ReadString(root, "camera");
        var label = ReadString(data, "label") ?? ReadString(root, "label");

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(camera) ||
            string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var zones = ReadStringArray(data, "entered_zones");
        var subLabel = ReadString(data, "sub_label");
        frigateEvent = new FrigateEvent(id, camera, label, eventType, zones, subLabel, root);
        return true;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }
}

# Frigate MQTT Push Listener

A small C#/.NET worker that listens to Frigate MQTT events, applies custom notification rules, and sends web-push notifications through Frigate's existing saved browser/device subscriptions.

The default configuration is intentionally safe for first rollout: `Listener:DryRun` is `true`, so the service logs would-be notifications instead of sending pushes.

## Features

- Dockerized .NET 8 worker service.
- MQTT subscription using `MQTTnet`.
- Frigate event parsing from `frigate/events`.
- Label and sub-label filtering.
- Optional notification when a Frigate `sub_label` appears later on an already-seen event.
- Per-camera cooldown and duplicate event suppression.
- Read-only Frigate SQLite subscription lookup.
- Web-push sending using Frigate's existing VAPID key material.
- Local Mosquitto broker in Docker Compose.

## Project Layout

```text
.
├── Dockerfile
├── docker-compose.yml
├── mosquitto/mosquitto.conf
└── src/FrigateMqttPushListener
    ├── Program.cs
    ├── appsettings.json
    ├── Events/
    ├── Filtering/
    ├── Mqtt/
    ├── Options/
    ├── Push/
    └── State/
```

## Configuration

Primary config lives in `src/FrigateMqttPushListener/appsettings.json` and can be overridden with environment variables.

Important settings:

```json
{
  "Listener": {
    "DryRun": true,
    "CooldownSeconds": 120,
    "AllowedLabels": [ "person" ],
    "IgnoredLabels": [ "bird", "mouse" ],
    "AllowedSubLabels": [],
    "IgnoredSubLabels": [],
    "NotifyOnSubLabelChange": true,
    "NotifyTypes": [ "new", "update" ]
  },
  "Mqtt": {
    "Host": "mosquitto",
    "Port": 1883,
    "Topic": "frigate/events"
  },
  "Push": {
    "FrigateDatabasePath": "/frigate/frigate.db",
    "NotificationsPemPath": "/frigate/notifications.pem",
    "Subject": "mailto:admin@example.com"
  }
}
```

Environment override examples:

```bash
Listener__DryRun=false
Listener__CooldownSeconds=180
Mqtt__Host=mosquitto
Push__Subject=mailto:admin@example.com
```

Docker Compose reads these values from `.env` if present. Start from the example file:

```bash
cp .env.example .env
```

Then update `FRIGATE_CONFIG_DIR` to the host directory that contains your Frigate `frigate.db`, `frigate.db-wal`, `frigate.db-shm`, and `notifications.pem`.

## Docker Compose

Start the broker and listener:

```bash
docker compose up -d --build
docker compose logs -f frigate-mqtt-push-listener
```

By default, the listener runs in dry-run mode:

```env
LISTENER_DRY_RUN=true
```

When logs show the expected notification decisions, enable real pushes:

```env
LISTENER_DRY_RUN=false
```

Then recreate the listener:

```bash
docker compose up -d --build frigate-mqtt-push-listener
```

## Frigate MQTT

Frigate needs MQTT enabled and must be able to reach the broker.

If using the included Docker Compose broker, attach Frigate to the `frigate-listener` Docker network and configure Frigate like this:

```yaml
mqtt:
  enabled: true
  host: mosquitto
  port: 1883
  topic_prefix: frigate
```

Do not expose MQTT to the public internet.

## Notification Templates

The title and body support simple placeholders:

- `{camera}`
- `{label}`
- `{sub_label}`
- `{person}`
- `{type}`
- `{id}`

`{person}` resolves to the Frigate `sub_label` when available, otherwise it falls back to the object label.

Example:

```json
{
  "TitleTemplate": "{camera}: {person} detected",
  "BodyTemplate": "{person} detected on {camera}"
}
```

## Development

Build locally:

```bash
dotnet build
```

Publish locally:

```bash
dotnet publish src/FrigateMqttPushListener/FrigateMqttPushListener.csproj -c Release
```

Run locally against a reachable broker:

```bash
Mqtt__Host=localhost \
Push__FrigateDatabasePath=/path/to/frigate.db \
Push__NotificationsPemPath=/path/to/notifications.pem \
dotnet run --project src/FrigateMqttPushListener
```

## Security Notes

- Keep `DryRun` enabled until real Frigate MQTT traffic has been observed and filtering is trusted.
- Mount Frigate database and VAPID key read-only.
- Do not remove or rotate Frigate's `notifications.pem`; existing browser/device subscriptions depend on it.
- Do not commit `.env`, Frigate databases, VAPID keys, logs, or listener state.
- Do not expose MQTT publicly.


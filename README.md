# Frigate MQTT Push Listener

A small C#/.NET worker that listens to Frigate MQTT events, applies custom notification rules, and sends web-push notifications through Frigate's existing saved browser/device subscriptions.

This project was made with Codex.

The default configuration is intentionally safe for first rollout: `Listener:DryRun` is `true`, so the service logs would-be notifications instead of sending pushes.

## Compatibility

Tested with Frigate `0.17.1-416a9b7`.

This project depends on Frigate's MQTT `frigate/events` payload and the SQLite `user.notification_tokens` storage used by Frigate web-push notifications. Other Frigate `0.17.x` builds are expected to work, but check the logs in dry-run mode before enabling real pushes.

## Features

- Dockerized .NET 8 worker service.
- Prebuilt container image published to GitHub Container Registry.
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
    "MinimumScore": 0.8,
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
Listener__MinimumScore=0.8
Mqtt__Host=mosquitto
Push__Subject=mailto:admin@example.com
```

Docker Compose reads these values from `.env` if present. Start from the example file:

```bash
cp .env.example .env
```

Then update `FRIGATE_CONFIG_DIR` to the host directory that contains your Frigate `frigate.db`, `frigate.db-wal`, `frigate.db-shm`, and `notifications.pem`.

Common Frigate config paths:

```text
/opt/frigate/config
/home/<user>/frigate/config
/path/to/your/frigate/config
```

The directory is mounted read-only into the listener container as `/frigate`.

## Quick Start

1. Create a project folder:

```bash
mkdir frigate-mqtt-push-listener
cd frigate-mqtt-push-listener
```

2. Download the Compose and environment examples:

```bash
curl -fsSLO https://raw.githubusercontent.com/darthza/FrigatePushNotificationHook/main/docker-compose.yml
curl -fsSLo .env.example https://raw.githubusercontent.com/darthza/FrigatePushNotificationHook/main/.env.example
cp .env.example .env
```

3. Edit `.env`:

```env
LISTENER_DRY_RUN=true
LISTENER_MINIMUM_SCORE=0.8
PUSH_SUBJECT=mailto:admin@example.com
FRIGATE_CONFIG_DIR=/opt/frigate/config
```

4. Start Mosquitto and the listener:

```bash
docker compose up -d
docker compose logs -f frigate-mqtt-push-listener
```

5. Enable MQTT in Frigate and restart Frigate.

6. Watch the listener logs. It should first run in dry-run mode and log decisions without sending pushes.

7. When the filters look right, set `LISTENER_DRY_RUN=false` in `.env` and recreate the listener:

```bash
docker compose up -d frigate-mqtt-push-listener
```

## Docker Compose

Start the broker and listener:

```bash
docker compose up -d
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
docker compose up -d frigate-mqtt-push-listener
```

## Standalone Container

If you already have an MQTT broker, you can run only the listener container.

Pull the image:

```bash
docker pull ghcr.io/darthza/frigatepushnotificationhook:latest
```

Run it:

```bash
mkdir -p ./state

docker run -d \
  --name frigate-mqtt-push-listener \
  --restart unless-stopped \
  --user 1000:1000 \
  -e Listener__DryRun=true \
  -e Listener__MinimumScore=0.8 \
  -e Mqtt__Host=<your-mqtt-host> \
  -e Mqtt__Port=1883 \
  -e Push__Subject=mailto:admin@example.com \
  -v /path/to/frigate/config:/frigate:ro \
  -v "$PWD/state:/app/state" \
  ghcr.io/darthza/frigatepushnotificationhook:latest
```

Watch logs:

```bash
docker logs -f frigate-mqtt-push-listener
```

After the dry-run output looks right, recreate the container with:

```bash
-e Listener__DryRun=false
```

## Image Tags

Images are published to GitHub Container Registry:

```text
ghcr.io/darthza/frigatepushnotificationhook
```

Available tag patterns:

- `latest`: current `main` branch build.
- `X.Y.Z`: exact release version.
- `X.Y`: release series tag.

For a stable deployment, pin a release tag instead of `latest`:

```yaml
image: ghcr.io/darthza/frigatepushnotificationhook:0.1.0
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

### If Frigate Runs In Docker Compose

If Frigate is managed by another Compose file, attach it to the same external network:

```yaml
services:
  frigate:
    networks:
      - frigate-listener

networks:
  frigate-listener:
    external: true
```

Then use `host: mosquitto` in Frigate's MQTT config.

### If Frigate Runs Outside This Docker Network

Run your own MQTT broker or publish this Compose broker only on a trusted LAN interface. Then point Frigate and the listener at that broker:

```env
MQTT_HOST=<your-mqtt-host>
MQTT_PORT=1883
```

Keep the broker private. MQTT should not be exposed to the public internet.

## Verifying It Works

Check containers:

```bash
docker compose ps
```

Watch listener logs:

```bash
docker compose logs -f frigate-mqtt-push-listener
```

Expected dry-run examples:

```text
Skipping Frigate event ...: label 'bird' is ignored
DRY RUN push: FrontYard: person detected - person detected on FrontYard
```

Optional synthetic test event:

```bash
docker exec frigate-listener-mosquitto mosquitto_pub \
  -h 127.0.0.1 \
  -t frigate/events \
  -m '{"type":"new","after":{"id":"test-event-1","camera":"TestCamera","label":"person","entered_zones":[]}}'
```

## Notification Templates

The title and body support simple placeholders:

- `{camera}`
- `{label}`
- `{sub_label}`
- `{sub_label_score}`
- `{person}`
- `{score}`
- `{type}`
- `{id}`

`{sub_label}` resolves to the known-person name when Frigate provides one.
`{sub_label_score}` resolves to the known-person confidence as a percentage, for example `99%`.
`{person}` resolves to the known-person name with confidence when available, for example `Pieter (99%)`, otherwise it falls back to the object label.
`{score}` resolves to the Frigate object confidence as a percentage, for example `83%`.

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

Build the container locally:

```bash
docker build -t frigate-mqtt-push-listener:local .
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

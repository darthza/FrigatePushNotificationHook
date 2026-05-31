FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/FrigateMqttPushListener/FrigateMqttPushListener.csproj src/FrigateMqttPushListener/
RUN dotnet restore src/FrigateMqttPushListener/FrigateMqttPushListener.csproj

COPY src/FrigateMqttPushListener src/FrigateMqttPushListener
RUN dotnet publish src/FrigateMqttPushListener/FrigateMqttPushListener.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

COPY --from=build /app/publish .
RUN mkdir -p /app/state /frigate

ENTRYPOINT ["dotnet", "FrigateMqttPushListener.dll"]

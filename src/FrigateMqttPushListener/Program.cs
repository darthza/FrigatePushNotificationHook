using FrigateMqttPushListener.Filtering;
using FrigateMqttPushListener.Mqtt;
using FrigateMqttPushListener.Options;
using FrigateMqttPushListener.Push;
using FrigateMqttPushListener.State;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ListenerOptions>(builder.Configuration.GetSection("Listener"));
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<PushOptions>(builder.Configuration.GetSection("Push"));
builder.Services.Configure<StateOptions>(builder.Configuration.GetSection("State"));

builder.Services.AddSingleton<NotificationFilter>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<FrigateSubscriptionStore>();
builder.Services.AddSingleton<WebPushNotificationSender>();
builder.Services.AddHostedService<FrigateMqttWorker>();

var host = builder.Build();
host.Run();

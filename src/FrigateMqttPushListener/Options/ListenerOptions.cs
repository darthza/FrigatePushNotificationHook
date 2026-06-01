namespace FrigateMqttPushListener.Options;

public sealed class ListenerOptions
{
    public bool DryRun { get; set; } = true;
    public int CooldownSeconds { get; set; } = 120;
    public double MinimumScore { get; set; } = 0.8;
    public string[] AllowedLabels { get; set; } = ["person"];
    public string[] IgnoredLabels { get; set; } = ["bird", "mouse"];
    public string[] AllowedSubLabels { get; set; } = [];
    public string[] IgnoredSubLabels { get; set; } = [];
    public bool NotifyOnSubLabelChange { get; set; } = true;
    public string[] NotifyTypes { get; set; } = ["new", "update", "end"];
    public bool RequireEnteredZones { get; set; }
    public string TitleTemplate { get; set; } = "{camera}: {label} detected";
    public string BodyTemplate { get; set; } = "{label} detected on {camera}";
}

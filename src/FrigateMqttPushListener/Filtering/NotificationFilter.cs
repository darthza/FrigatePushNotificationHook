using FrigateMqttPushListener.Events;
using FrigateMqttPushListener.Options;
using Microsoft.Extensions.Options;

namespace FrigateMqttPushListener.Filtering;

public sealed class NotificationFilter
{
    private readonly IOptionsMonitor<ListenerOptions> _options;

    public NotificationFilter(IOptionsMonitor<ListenerOptions> options)
    {
        _options = options;
    }

    public bool ShouldNotify(FrigateEvent frigateEvent, out string reason)
    {
        var options = _options.CurrentValue;
        var allowed = options.AllowedLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignored = options.IgnoredLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notifyTypes = options.NotifyTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ignored.Contains(frigateEvent.Label))
        {
            reason = $"label '{frigateEvent.Label}' is ignored";
            return false;
        }

        if (allowed.Count > 0 && !allowed.Contains(frigateEvent.Label))
        {
            reason = $"label '{frigateEvent.Label}' is not allowed";
            return false;
        }

        if (options.MinimumScore > 0)
        {
            if (frigateEvent.Score is null)
            {
                reason = $"event has no score and minimum score is {options.MinimumScore:P0}";
                return false;
            }

            if (frigateEvent.Score < options.MinimumScore)
            {
                reason = $"score {frigateEvent.Score:P0} is below minimum {options.MinimumScore:P0}";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(frigateEvent.SubLabel))
        {
            var ignoredSub = options.IgnoredSubLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allowedSub = options.AllowedSubLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (ignoredSub.Contains(frigateEvent.SubLabel))
            {
                reason = $"sub_label '{frigateEvent.SubLabel}' is ignored";
                return false;
            }

            if (allowedSub.Count > 0 && !allowedSub.Contains(frigateEvent.SubLabel))
            {
                reason = $"sub_label '{frigateEvent.SubLabel}' is not allowed";
                return false;
            }
        }
        else if (options.AllowedSubLabels.Length > 0)
        {
            var allowedSub = options.AllowedSubLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!allowedSub.Contains("unknown"))
            {
                reason = "event has no sub_label and 'unknown' is not in AllowedSubLabels";
                return false;
            }
        }

        if (notifyTypes.Count > 0 && !notifyTypes.Contains(frigateEvent.Type))
        {
            reason = $"event type '{frigateEvent.Type}' is not configured for notifications";
            return false;
        }

        if (options.RequireEnteredZones && frigateEvent.EnteredZones.Count == 0)
        {
            reason = "event has not entered a zone";
            return false;
        }

        reason = "matched notification rules";
        return true;
    }
}

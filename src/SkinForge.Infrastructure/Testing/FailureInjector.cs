using SkinForge.Core.Interfaces;

namespace SkinForge.Infrastructure.Testing;

public class FailureInjector : IFailureInjector
{
    private FailureTrigger _activeTriggers = FailureTrigger.None;
    private readonly object _lock = new();

    public bool IsEnabled { get; set; } = false;

    public void EnableFailure(FailureTrigger trigger)
    {
        lock (_lock)
        {
            _activeTriggers |= trigger;
            IsEnabled = true;
        }
    }

    public void DisableFailure(FailureTrigger trigger)
    {
        lock (_lock)
        {
            _activeTriggers &= ~trigger;
            if (_activeTriggers == FailureTrigger.None)
            {
                IsEnabled = false;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _activeTriggers = FailureTrigger.None;
            IsEnabled = false;
        }
    }

    public void ThrowIfTriggered(FailureTrigger trigger, string? context = null)
    {
        if (!IsEnabled) return;

        lock (_lock)
        {
            if ((_activeTriggers & trigger) == trigger)
            {
                var msg = $"[FAILURE INJECTION] Simulated {trigger} triggered in context: {context ?? "Operation"}";
                switch (trigger)
                {
                    case FailureTrigger.DiskWriteFailure:
                        throw new IOException(msg);
                    case FailureTrigger.DatabaseFailure:
                        throw new InvalidOperationException($"Database locked or corrupted: {msg}");
                    case FailureTrigger.MissingFile:
                        throw new FileNotFoundException(msg);
                    case FailureTrigger.InvalidManifest:
                        throw new FormatException($"Malformed schema simulation: {msg}");
                    case FailureTrigger.Timeout:
                        throw new TimeoutException($"Simulated operation timeout: {msg}");
                    case FailureTrigger.InterruptedOperation:
                        throw new OperationCanceledException($"Simulated process termination / kill: {msg}");
                    default:
                        throw new Exception(msg);
                }
            }
        }
    }
}

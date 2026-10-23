namespace SkinForge.Core.Interfaces;

[Flags]
public enum FailureTrigger
{
    None = 0,
    DiskWriteFailure = 1 << 0,
    DatabaseFailure = 1 << 1,
    MissingFile = 1 << 2,
    InvalidManifest = 1 << 3,
    Timeout = 1 << 4,
    InterruptedOperation = 1 << 5
}

public interface IFailureInjector
{
    bool IsEnabled { get; set; }
    void EnableFailure(FailureTrigger trigger);
    void DisableFailure(FailureTrigger trigger);
    void Reset();
    void ThrowIfTriggered(FailureTrigger trigger, string? context = null);
}

using SkinForge.Core.Interfaces;
using SkinForge.Infrastructure.Testing;
using Xunit;

namespace SkinForge.Tests;

public class FailureInjectionTests
{
    [Fact]
    public void Disabled_FailureInjector_Does_Not_Throw()
    {
        var injector = new FailureInjector();
        injector.ThrowIfTriggered(FailureTrigger.DiskWriteFailure, "Writing DB");
        Assert.False(injector.IsEnabled);
    }

    [Fact]
    public void Enabled_DiskWriteFailure_Throws_IOException()
    {
        var injector = new FailureInjector();
        injector.EnableFailure(FailureTrigger.DiskWriteFailure);

        Assert.True(injector.IsEnabled);
        Assert.Throws<IOException>(() => injector.ThrowIfTriggered(FailureTrigger.DiskWriteFailure, "Extracting Package"));

        // Other triggers shouldn't fire if not enabled
        injector.ThrowIfTriggered(FailureTrigger.Timeout, "Network check");

        injector.Reset();
        Assert.False(injector.IsEnabled);
    }

    [Fact]
    public void InterruptedOperation_Throws_OperationCanceledException()
    {
        var injector = new FailureInjector();
        injector.EnableFailure(FailureTrigger.InterruptedOperation);

        Assert.Throws<OperationCanceledException>(() => injector.ThrowIfTriggered(FailureTrigger.InterruptedOperation, "Mod Staging"));
    }
}

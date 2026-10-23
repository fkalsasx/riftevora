namespace SkinForge.Infrastructure.SystemInfo;

/// <summary>One switch for "never touch the network". Everything that would download something asks here first.</summary>
public static class NetworkPolicy
{
    public static volatile bool Offline;
}

using Microsoft.Extensions.Options;

namespace Wapper.Tests.Fakes;

/// <summary>Hands out the same options instance for every tenant name.</summary>
internal sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue => value;

    public TOptions Get(string? name) => value;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}

using Microsoft.Extensions.Time.Testing;

namespace Wapper.Tests.Fakes;

internal static class Clock
{
    /// <summary>
    /// Runs an operation that sleeps on the fake clock, winding the clock forward until it
    /// finishes.
    /// </summary>
    /// <remarks>
    /// A fake clock only moves when something moves it, so an operation waiting on it would
    /// otherwise never wake. There is no way to be told that the code under test has reached
    /// its delay, so the clock is nudged forward in small steps with a yield in between.
    /// The steps are virtual: winding through Meta's 64-second backoff costs milliseconds.
    /// </remarks>
    public static async Task<T> RunAsync<T>(FakeTimeProvider time, Task<T> operation)
    {
        for (var i = 0; i < 2000 && !operation.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Yield();
        }

        // Awaiting rethrows whatever the operation threw, which is what most of these tests
        // are actually asserting on.
        return await operation;
    }

    /// <inheritdoc cref="RunAsync{T}(FakeTimeProvider, Task{T})" />
    public static async Task RunAsync(FakeTimeProvider time, Task operation)
    {
        for (var i = 0; i < 2000 && !operation.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Yield();
        }

        await operation;
    }
}

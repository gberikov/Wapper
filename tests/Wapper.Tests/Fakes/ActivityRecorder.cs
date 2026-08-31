using System.Diagnostics;

namespace Wapper.Tests.Fakes;

/// <summary>
/// Listens to the library's <see cref="ActivitySource"/> and keeps what it emitted.
/// </summary>
/// <remarks>
/// An <see cref="ActivitySource"/> produces nothing at all until something samples it, so a
/// test that wants to see a span has to be that something.
/// </remarks>
internal sealed class ActivityRecorder : IDisposable
{
    private readonly ActivityListener _listener;

    public ActivityRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WhatsAppDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Activities.Add(activity),
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Every span the library finished while this was listening, in order.</summary>
    public List<Activity> Activities { get; } = [];

    public void Dispose() => _listener.Dispose();
}

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Wapper.Tests.Fakes;

/// <summary>
/// Listens to the library's <see cref="ActivitySource"/> and keeps the spans this test
/// produced.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="ActivitySource"/> produces nothing at all until something samples it, so a
/// test that wants to see a span has to be that something.
/// </para>
/// <para>
/// A listener is process-wide, and the tests run in parallel, so it also hears every span
/// every other test is emitting at the same time. Only the ones underneath this recorder's
/// own root are kept: an <see cref="Activity"/> inherits its parent's trace id through the
/// ambient <see cref="Activity.Current"/>, which flows down the test's async calls and
/// nobody else's.
/// </para>
/// </remarks>
internal sealed class ActivityRecorder : IDisposable
{
    /// <summary>Named so nothing mistakes it for the library's own source.</summary>
    /// <remarks>
    /// A constant, not a read of <see cref="TestSource"/>: creating an
    /// <see cref="ActivitySource"/> calls every registered listener back synchronously, and
    /// the field it is being assigned to is still null while that happens.
    /// </remarks>
    private const string TestSourceName = "Wapper.Tests.ActivityRecorder";

    private static readonly ActivitySource TestSource = new(TestSourceName);

    private readonly ConcurrentQueue<Activity> _heard = new();
    private readonly ActivityListener _listener;
    private readonly Activity _root;

    public ActivityRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name == WhatsAppDiagnostics.ActivitySourceName || source.Name == TestSourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _heard.Enqueue,
        };

        // Added before the root is started, or the root is never sampled and has no trace id
        // to match anything against.
        ActivitySource.AddActivityListener(_listener);

        // Never null: the listener above samples everything. Asserted rather than guarded,
        // because a null root would quietly turn the filter below into "keep everything",
        // which is the flakiness this class exists to remove.
        _root = TestSource.StartActivity("test")
            ?? throw new InvalidOperationException("The recorder's own listener did not sample its root activity.");
    }

    /// <summary>
    /// The spans the code under test produced, in the order they finished.
    /// </summary>
    /// <remarks>
    /// Read after the call being measured has returned: a span is only heard once it stops.
    /// </remarks>
    public IReadOnlyList<Activity> Activities =>
        [.. _heard.Where(activity => activity.TraceId == _root.TraceId && activity != _root)];

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }
}

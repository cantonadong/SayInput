using System.Collections.ObjectModel;
using System.Diagnostics;

namespace VoiceTyper.Core.Performance;

public sealed class PerformanceMetrics : IPerformanceMetrics
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, (long Start, Dictionary<PerformanceEvent, TimeSpan> Values)> sessions = new();
    public void Mark(Guid sessionId, PerformanceEvent performanceEvent)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(sessionId, out var session))
                sessions[sessionId] = session = (Stopwatch.GetTimestamp(), new());
            session.Values.TryAdd(performanceEvent, Stopwatch.GetElapsedTime(session.Start));
        }
    }
    public SessionPerformanceMetrics Snapshot(Guid sessionId)
    {
        lock (gate) return new(sessionId, new ReadOnlyDictionary<PerformanceEvent, TimeSpan>(
            sessions.TryGetValue(sessionId, out var session) ? new Dictionary<PerformanceEvent, TimeSpan>(session.Values) : new Dictionary<PerformanceEvent, TimeSpan>()));
    }
    public void Clear(Guid sessionId) { lock (gate) sessions.Remove(sessionId); }
}

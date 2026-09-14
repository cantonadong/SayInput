namespace VoiceTyper.Core.Performance;

/// <summary>Implementations measure elapsed time with a monotonic clock.</summary>
public interface IPerformanceMetrics
{
    void Mark(Guid sessionId, PerformanceEvent performanceEvent);
    SessionPerformanceMetrics Snapshot(Guid sessionId);
    void Clear(Guid sessionId);
}

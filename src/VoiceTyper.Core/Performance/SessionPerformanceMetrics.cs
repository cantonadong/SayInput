namespace VoiceTyper.Core.Performance;

/// <summary>Elapsed times from session start; snapshots must not change after publication.</summary>
public sealed record SessionPerformanceMetrics(Guid SessionId, IReadOnlyDictionary<PerformanceEvent, TimeSpan> Milestones);

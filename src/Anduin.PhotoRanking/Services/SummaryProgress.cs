namespace Anduin.PhotoRanking.Services;

public sealed record SummaryGenerationStatus
{
    public int? PhotoId { get; init; }
    public int Round { get; init; }
    public int CompletedRounds { get; init; }
    public string Phase { get; init; } = "idle";
    public DateTime? StartedAt { get; init; }
    public DateTime? FirstTokenAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public DateTime? NextRetryAt { get; init; }
    public string? Error { get; init; }

    // First-token wait includes gateway queueing and image/prompt prefill.
    public double WaitingSeconds => StartedAt == null ? 0 :
        Math.Max(0, ((FirstTokenAt ?? FinishedAt ?? DateTime.UtcNow) - StartedAt.Value).TotalSeconds);

    public double GeneratingSeconds => FirstTokenAt == null ? 0 :
        Math.Max(0, ((FinishedAt ?? DateTime.UtcNow) - FirstTokenAt.Value).TotalSeconds);
}

// Runtime telemetry only. Completed rounds are checkpointed in the photo database.
public sealed class SummaryProgress
{
    private readonly object _gate = new();
    private DateTime? _version;
    private CancellationTokenSource? _active;
    private SummaryGenerationStatus _status = Idle();

    private static SummaryGenerationStatus Idle() => new();

    public SummaryGenerationStatus Get(DateTime version)
    {
        lock (_gate) return _version == version ? _status : Idle();
    }

    public void Invalidate(DateTime version)
    {
        lock (_gate)
        {
            if (_version == version) return;
            _active?.Cancel();
            _status = Idle();
            _version = version;
        }
    }

    public void BeginPhoto(int photoId, DateTime version, int completed, CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            _version = version;
            _active = cancellation;
            _status = Idle() with { PhotoId = photoId, CompletedRounds = completed };
        }
    }

    public void EndPhoto(CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_active, cancellation)) _active = null;
        }
    }

    public void BeginRound(int round)
    {
        lock (_gate) _status = _status with
        {
            Round = round, Phase = "waiting", StartedAt = DateTime.UtcNow,
            FirstTokenAt = null, FinishedAt = null, NextRetryAt = null, Error = null
        };
    }

    public void TokenReceived()
    {
        lock (_gate)
        {
            if (_status.FirstTokenAt == null)
                _status = _status with { FirstTokenAt = DateTime.UtcNow, Phase = "generating" };
        }
    }

    public void RoundSaved(int round)
    {
        lock (_gate) _status = _status with
        {
            CompletedRounds = round, Phase = round == 3 ? "completed" : "saved", FinishedAt = DateTime.UtcNow
        };
    }

    public void Failed(string message, int retryDelaySeconds)
    {
        lock (_gate) _status = _status with
        {
            Phase = "retrying", Error = message, FinishedAt = DateTime.UtcNow,
            NextRetryAt = DateTime.UtcNow.AddSeconds(retryDelaySeconds)
        };
    }
}

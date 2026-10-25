using System.Diagnostics;
using Entities.Exceptions;
using Microsoft.Extensions.Logging;

namespace Repository;

internal sealed class ForMeRepositoryTelemetry(ILogger logger, string eventName)
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, double> _durations = [];
    public string Stage { get; private set; } = "";
    public string Outcome { get; private set; } = "Succeeded";
    public LogLevel Level { get; private set; } = LogLevel.Information;
    public string? FailureType { get; private set; }
    public string? Mode { get; set; }
    public double? AppliedAnnRandomPageCost { get; set; }
    public int? ResolvedExcludedTagCount { get; set; }
    public int? ConfiguredRepresentativeCount { get; set; }
    public int? AnnCandidateLimit { get; set; }
    public int? AnnCandidateRowCount { get; set; }
    public int? ExactTopUpQueryCount { get; set; }
    public int? ExactTopUpImageCount { get; set; }
    public int? SeedCount { get; set; }
    public int? RepresentativeCount { get; set; }
    public int? NeighborQueryCount { get; set; }
    public int? StoredImageCount { get; set; }
    public int? ReturnedImageCount { get; set; }
    public bool? HasMore { get; set; }

    public IDisposable Measure(string stage)
    {
        Stage = stage;
        return new Measurement(this, stage);
    }

    public void Failed(Exception exception)
    {
        FailureType = exception.GetType().Name;
        (Outcome, Level) = exception switch
        {
            ForMeFeedLimitException or Base401UnauthorizedException or Base409ConflictException => ("Rejected", LogLevel.Warning),
            OperationCanceledException => ("Canceled", LogLevel.Warning),
            _ => ("Failed", LogLevel.Error)
        };
    }

    public void Emit()
    {
        var fields = new List<KeyValuePair<string, object?>>
        {
            new("EventName", eventName), new("Outcome", Outcome), new("Stage", Stage),
            new("RepositoryElapsedMilliseconds", Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds),
            new("{OriginalFormat}", "{EventName}")
        };
        foreach (var (stage, value) in _durations) fields.Add(new(stage + "ElapsedMilliseconds", value));
        if (FailureType is not null) fields.Add(new("FailureType", FailureType));
        if (Mode is not null && (eventName != "ForMeFeedPageRead" || Outcome == "Succeeded"))
            fields.Add(new("Mode", Mode));
        if (AppliedAnnRandomPageCost is not null) fields.Add(new("AppliedAnnRandomPageCost", AppliedAnnRandomPageCost));
        if (ResolvedExcludedTagCount is not null) fields.Add(new("ResolvedExcludedTagCount", ResolvedExcludedTagCount));
        if (ConfiguredRepresentativeCount is not null) fields.Add(new("ConfiguredRepresentativeCount", ConfiguredRepresentativeCount));
        if (AnnCandidateLimit is not null) fields.Add(new("AnnCandidateLimit", AnnCandidateLimit));
        if (AnnCandidateRowCount is not null) fields.Add(new("AnnCandidateRowCount", AnnCandidateRowCount));
        if (ExactTopUpQueryCount is not null) fields.Add(new("ExactTopUpQueryCount", ExactTopUpQueryCount));
        if (ExactTopUpImageCount is not null) fields.Add(new("ExactTopUpImageCount", ExactTopUpImageCount));
        if (SeedCount is not null) fields.Add(new("SeedCount", SeedCount));
        if (RepresentativeCount is not null) fields.Add(new("RepresentativeCount", RepresentativeCount));
        if (NeighborQueryCount is not null) fields.Add(new("NeighborQueryCount", NeighborQueryCount));
        if (StoredImageCount is not null) fields.Add(new("StoredImageCount", StoredImageCount));
        if (Outcome == "Succeeded" && ReturnedImageCount is not null) fields.Add(new("ReturnedImageCount", ReturnedImageCount));
        if (Outcome == "Succeeded" && HasMore is not null) fields.Add(new("HasMore", HasMore));
        logger.Log(Level, new EventId(0, eventName), fields, null, (_, _) => eventName);
    }

    private sealed class Measurement(ForMeRepositoryTelemetry owner, string stage) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._durations[stage] = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        }
    }
}

using System.Diagnostics.Metrics;

namespace TalentSuite.Server.Bids.Services;

public sealed class TalentSuiteMetrics
{
    private readonly Counter<int> _ingestionJobsStarted;
    private readonly Counter<int> _ingestionJobsFinished;
    private readonly Histogram<double> _ingestionJobDuration;
    private readonly UpDownCounter<int> _activeIngestionJobs;

    public TalentSuiteMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("TalentSuite.Server");

        _ingestionJobsStarted = meter.CreateCounter<int>(
            "talentsuite.ingestion.jobs.started",
            "{jobs}",
            "Number of document ingestion jobs started");

        _ingestionJobsFinished = meter.CreateCounter<int>(
            "talentsuite.ingestion.jobs.finished",
            "{jobs}",
            "Number of document ingestion jobs finished");

        _ingestionJobDuration = meter.CreateHistogram<double>(
            "talentsuite.ingestion.job.duration",
            "s",
            "Document ingestion end-to-end duration",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [5, 15, 30, 60, 90, 120, 180, 300]
            });

        _activeIngestionJobs = meter.CreateUpDownCounter<int>(
            "talentsuite.ingestion.jobs.active",
            "{jobs}",
            "Currently active document ingestion jobs");
    }

    public void JobStarted()
    {
        _ingestionJobsStarted.Add(1);
        _activeIngestionJobs.Add(1);
    }

    public void JobFinished(double durationSeconds, bool succeeded)
    {
        _activeIngestionJobs.Add(-1);
        _ingestionJobsFinished.Add(1, new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed"));
        _ingestionJobDuration.Record(durationSeconds, new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed"));
    }
}

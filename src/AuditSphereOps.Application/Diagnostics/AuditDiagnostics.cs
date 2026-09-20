using System.Diagnostics;
using System.Diagnostics.Metrics;
using AuditSphereOps.Domain.Completion;

namespace AuditSphereOps.Application.Diagnostics;

/// <summary>
/// Low-cardinality diagnostic primitives shared by web and worker composition.
/// These signals are operational telemetry only; immutable audit evidence remains
/// in the database and is never replaced by a span or metric.
/// </summary>
public static class AuditDiagnostics
{
  public const string ActivitySourceName = "AuditSphereOps.Application";
  public const string MeterName = "AuditSphereOps.Application";

  public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
  public static readonly Meter Meter = new(MeterName);

  public static readonly Counter<long> OperationsClaimed =
    Meter.CreateCounter<long>("auditsphere.operations.claimed", unit: "{operation}");
  public static readonly Counter<long> OperationsCompleted =
    Meter.CreateCounter<long>("auditsphere.operations.completed", unit: "{operation}");
  public static readonly Counter<long> OperationsDispositioned =
    Meter.CreateCounter<long>("auditsphere.operations.dispositioned", unit: "{operation}");
  public static readonly Histogram<double> OperationDuration =
    Meter.CreateHistogram<double>("auditsphere.operations.duration", unit: "s");
  public static readonly Histogram<double> DraftSaveDuration =
    Meter.CreateHistogram<double>("auditsphere.workpaper.draft.save.duration", unit: "s");

  public static Activity? StartCommand(string commandName) =>
    ActivitySource.StartActivity(commandName, ActivityKind.Internal);

  public static void RecordClaimed(DurableOperation operation) =>
    OperationsClaimed.Add(1, Tags(operation));

  public static void RecordCompleted(DurableOperation operation) =>
    OperationsCompleted.Add(1, Tags(operation));

  public static void RecordDispositioned(DurableOperation operation, string disposition) =>
    OperationsDispositioned.Add(1,
      new KeyValuePair<string, object?>[]
      {
        new("operation.kind", operation.OperationKind),
        new("execution.mode", operation.ExecutionMode.ToString()),
        new("disposition", disposition)
      });

  public static void RecordOperationDuration(DurableOperation operation, double seconds, string outcome) =>
    OperationDuration.Record(Math.Max(0, seconds),
      new KeyValuePair<string, object?>[]
      {
        new("operation.kind", operation.OperationKind),
        new("execution.mode", operation.ExecutionMode.ToString()),
        new("outcome", outcome)
      });

  private static KeyValuePair<string, object?>[] Tags(DurableOperation operation) =>
  [
    new("operation.kind", operation.OperationKind),
    new("execution.mode", operation.ExecutionMode.ToString())
  ];
}

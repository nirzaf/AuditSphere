# AuditSphere operational telemetry

The Web and Worker hosts expose OpenTelemetry `ActivitySource` and `Meter` signals. HTTP spans, Npgsql spans/metrics, runtime metrics, command spans, durable-operation attempt spans, and draft-save timing are diagnostic signals only. Immutable audit events, workpaper submissions, and operation state remain PostgreSQL records.

Set `Telemetry:Otlp:Endpoint` through the deployment configuration when an approved collector is available. The setting is independent from `ExternalEffects:Enabled`; enabling telemetry does not authorize Microsoft Graph, SharePoint, Purview, or any other provider effect. If no endpoint is configured, the application still starts with the sources and meters registered but does not export.

Recommended dashboard panels:

- request rate, error rate, and latency by route class;
- command duration and error-code outcome by bounded command name;
- durable-operation claim, completion, disposition, and duration by operation kind and execution mode;
- active database sessions, idle-in-transaction sessions, transaction duration, and pool wait indicators;
- draft-save success, conflict, idempotent retry, and save latency.

Do not use user IDs, client IDs, engagement IDs, operation IDs, upload IDs, correlation IDs, tokens, request bodies, draft text, connection strings, or provider payloads as time-series labels. Correlation and operation identifiers may remain in structured logs/spans only where needed for a bounded incident trace; secrets and confidential content are never logged or exported.

Collector failure is intentionally non-authoritative: exporter queues and timeouts are outside the business transaction, and a telemetry outage must not roll back a durable command or alter its result.

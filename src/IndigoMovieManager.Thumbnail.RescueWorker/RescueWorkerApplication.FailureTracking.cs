using System;
using System.Text.Json;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        private static void AppendRescueAttemptRecord(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            string engineId,
            ThumbnailFailureKind failureKind,
            string failureReason,
            long elapsedMs,
            string routeId,
            string symptomClass,
            bool repairApplied,
            string sourceMovieFullPathOverride,
            int attemptNo
        )
        {
            string traceId = TryExtractTraceId(leasedRecord?.ExtraJson);
            DateTime nowUtc = DateTime.UtcNow;
            _ = failureDbService.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MainDbFullPath = leasedRecord.MainDbFullPath,
                    MainDbPathHash = leasedRecord.MainDbPathHash,
                    MoviePath = leasedRecord.MoviePath,
                    MoviePathKey = leasedRecord.MoviePathKey,
                    TabIndex = leasedRecord.TabIndex,
                    Lane = "rescue",
                    AttemptGroupId = leasedRecord.AttemptGroupId,
                    AttemptNo = attemptNo,
                    // 試行ログは open request の本体ではないので、完了済み失敗として固定する。
                    Status = "attempt_failed",
                    LeaseOwner = "",
                    LeaseUntilUtc = "",
                    Engine = engineId,
                    FailureKind = failureKind,
                    FailureReason = failureReason ?? "",
                    ElapsedMs = Math.Max(0, elapsedMs),
                    SourcePath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                        ? leasedRecord.MoviePath
                        : sourceMovieFullPathOverride,
                    RepairApplied = repairApplied,
                    ResultSignature = "",
                    ExtraJson = BuildAttemptExtraJson(
                        engineId,
                        repairApplied,
                        routeId,
                        symptomClass,
                        traceId
                    ),
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                }
            );
        }

        // p1段階では fixed 順のままでも、親行だけ見れば進行中 phase を追えるようにする。
        private static void UpdateProgressSnapshot(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            int attemptNo,
            string routeId,
            string symptomClass,
            string sourceMovieFullPath,
            ThumbnailFailureKind currentFailureKind,
            string currentFailureReason
        )
        {
            string traceId = TryExtractTraceId(leasedRecord?.ExtraJson);
            _ = failureDbService.UpdateProcessingSnapshot(
                leasedRecord.FailureId,
                leaseOwner,
                DateTime.UtcNow,
                BuildProgressExtraJson(
                    phase,
                    engineId,
                    repairApplied,
                    detail,
                    attemptNo,
                    routeId,
                    symptomClass,
                    sourceMovieFullPath,
                    currentFailureKind,
                    currentFailureReason,
                    traceId
                )
            );
        }

        private static void WriteRescueTrace(
            ThumbnailFailureRecord leasedRecord,
            string dbName,
            string thumbFolder,
            string action,
            string result,
            string routeId = "",
            string symptomClass = "",
            string phase = "",
            string engine = "",
            long elapsedMs = -1,
            ThumbnailFailureKind failureKind = ThumbnailFailureKind.None,
            string reason = "",
            string outputPath = ""
        )
        {
            string traceId = TryExtractTraceId(leasedRecord?.ExtraJson);
            ThumbnailRescueTraceLog.Write(
                source: "worker",
                action: action,
                result: result,
                failureId: leasedRecord?.FailureId ?? 0,
                moviePath: leasedRecord?.MoviePath ?? "",
                tabIndex: leasedRecord?.TabIndex ?? -1,
                panelSize: ThumbnailRescueTraceLog.BuildPanelSizeLabel(
                    leasedRecord?.TabIndex ?? -1,
                    dbName ?? "",
                    thumbFolder ?? ""
                ),
                routeId: routeId ?? "",
                symptomClass: symptomClass ?? "",
                phase: phase ?? "",
                engine: engine ?? "",
                elapsedMs: elapsedMs,
                failureKind: failureKind == ThumbnailFailureKind.None ? "" : failureKind.ToString(),
                reason: reason ?? "",
                outputPath: outputPath ?? ""
            );
            ThumbnailMovieTraceLog.Write(
                traceId,
                source: "rescue-worker",
                phase: string.IsNullOrWhiteSpace(phase) ? action : phase,
                moviePath: leasedRecord?.MoviePath ?? "",
                tabIndex: leasedRecord?.TabIndex ?? -1,
                engine: engine ?? "",
                result: result ?? "",
                detail: $"action={action}; reason={reason ?? ""}",
                outputPath: outputPath ?? "",
                routeId: routeId ?? "",
                symptomClass: symptomClass ?? "",
                failureId: leasedRecord?.FailureId ?? 0,
                attemptNo: leasedRecord?.AttemptNo ?? 0
            );
        }

        private static string BuildAttemptExtraJson(
            string engineId,
            bool repairApplied,
            string routeId,
            string symptomClass,
            string traceId = ""
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    RouteId = routeId ?? FixedRouteId,
                    SymptomClass = symptomClass ?? UnclassifiedSymptomClass,
                    EngineForced = engineId ?? "",
                    RepairApplied = repairApplied,
                    TraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(traceId),
                }
            );
        }

        private static string BuildProgressExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            int attemptNo,
            string routeId,
            string symptomClass,
            string sourceMovieFullPath,
            ThumbnailFailureKind currentFailureKind,
            string currentFailureReason,
            string traceId = ""
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    RouteId = routeId ?? FixedRouteId,
                    SymptomClass = symptomClass ?? UnclassifiedSymptomClass,
                    CurrentPhase = phase ?? "",
                    CurrentEngine = engineId ?? "",
                    RepairApplied = repairApplied,
                    AttemptNo = attemptNo,
                    SourcePath = sourceMovieFullPath ?? "",
                    CurrentFailureKind = currentFailureKind == ThumbnailFailureKind.None
                        ? ""
                        : currentFailureKind.ToString(),
                    CurrentFailureReason = currentFailureReason ?? "",
                    Detail = detail ?? "",
                    TraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(traceId),
                }
            );
        }

        private static string BuildTerminalExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            string traceId = ""
        )
        {
            return BuildTerminalExtraJson(
                phase,
                engineId,
                repairApplied,
                detail,
                FixedRouteId,
                UnclassifiedSymptomClass,
                traceId
            );
        }

        private static string BuildTerminalExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail,
            string routeId,
            string symptomClass,
            string traceId = ""
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    RouteId = routeId ?? FixedRouteId,
                    SymptomClass = symptomClass ?? UnclassifiedSymptomClass,
                    Phase = phase ?? "",
                    EngineForced = engineId ?? "",
                    RepairApplied = repairApplied,
                    Detail = detail ?? "",
                    TraceId = ThumbnailMovieTraceRuntime.NormalizeTraceId(traceId),
                }
            );
        }

        private static string TryExtractTraceId(string extraJson)
        {
            if (string.IsNullOrWhiteSpace(extraJson))
            {
                return "";
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return "";
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (
                        !string.Equals(property.Name, "trace_id", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(property.Name, "TraceId", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return "";
                    }

                    return ThumbnailMovieTraceRuntime.NormalizeTraceId(
                        property.Value.GetString()
                    );
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private static string TryExtractRescueMode(string extraJson)
        {
            if (string.IsNullOrWhiteSpace(extraJson))
            {
                return "";
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return "";
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (
                        !string.Equals(property.Name, "rescue_mode", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(property.Name, "RescueMode", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return "";
                    }

                    return NormalizeRescueMode(property.Value.GetString());
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private static bool ShouldReplaceThumbnailWhenMetadataMissing(string extraJson)
        {
            if (string.IsNullOrWhiteSpace(extraJson))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (
                        !string.Equals(
                            property.Name,
                            "replace_if_metadata_missing",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !string.Equals(
                            property.Name,
                            "ReplaceIfMetadataMissing",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.True)
                    {
                        return true;
                    }

                    if (property.Value.ValueKind == JsonValueKind.False)
                    {
                        return false;
                    }

                    if (
                        property.Value.ValueKind == JsonValueKind.String
                        && bool.TryParse(property.Value.GetString(), out bool parsed)
                    )
                    {
                        return parsed;
                    }

                    return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}

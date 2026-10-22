// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using k8s.Models;
using System.Text.Json;

namespace Services.WebApi
{
    /// <summary>
    /// A KEDA ScaledObject as read from the cluster: what it targets and whether KEDA has acknowledged a pause.
    /// </summary>
    internal sealed record KedaScaledObject(
        string Name,
        string Uid,
        string TargetName,
        IReadOnlyDictionary<string, string> Annotations,
        IReadOnlyDictionary<string, string> Conditions)
    {
        public const string PausedReplicasAnnotation = "autoscaling.keda.sh/paused-replicas";
        public const string PausedAnnotation = "autoscaling.keda.sh/paused";

        public bool HasPauseRequest => Annotations.ContainsKey(PausedReplicasAnnotation) ||
                                       (Annotations.TryGetValue(PausedAnnotation, out var paused) && string.Equals(paused, "true", StringComparison.OrdinalIgnoreCase));

        // Raw JSON merge patches: null removes an annotation, which a serialized object would drop.
        public static V1Patch PauseAtZeroPatch() => AnnotationPatch($"{{\"{PausedReplicasAnnotation}\":\"0\",\"{PausedAnnotation}\":null}}");

        // KEDA leaves its Paused condition True when the pause annotations are merely removed, so resume sets
        // paused to "false", which makes it report the unpause.
        public static V1Patch ResumePatch() => AnnotationPatch($"{{\"{PausedReplicasAnnotation}\":null,\"{PausedAnnotation}\":\"false\"}}");

        private static V1Patch AnnotationPatch(string annotationsJson) =>
            new V1Patch($"{{\"metadata\":{{\"annotations\":{annotationsJson}}}}}", V1Patch.PatchType.MergePatch);

        public string? Condition(string type) => Conditions.TryGetValue(type, out var status) ? status : null;

        public static KedaScaledObject Parse(JsonElement item)
        {
            var metadata = item.GetProperty("metadata");
            var name = metadata.GetProperty("name").GetString() ?? string.Empty;
            var uid = metadata.TryGetProperty("uid", out var uidElement) ? uidElement.GetString() ?? string.Empty : string.Empty;

            var annotations = new Dictionary<string, string>(StringComparer.Ordinal);
            if (metadata.TryGetProperty("annotations", out var annotationsElement) && annotationsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var annotation in annotationsElement.EnumerateObject())
                {
                    annotations[annotation.Name] = annotation.Value.ValueKind == JsonValueKind.String ? annotation.Value.GetString() ?? string.Empty : annotation.Value.ToString();
                }
            }

            var target = item.TryGetProperty("spec", out var spec) && spec.TryGetProperty("scaleTargetRef", out var targetRef) && targetRef.TryGetProperty("name", out var targetName)
                ? targetName.GetString() ?? string.Empty
                : string.Empty;

            var conditions = new Dictionary<string, string>(StringComparer.Ordinal);
            if (item.TryGetProperty("status", out var status) && status.TryGetProperty("conditions", out var conditionsElement) && conditionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var condition in conditionsElement.EnumerateArray())
                {
                    if (condition.TryGetProperty("type", out var type) && condition.TryGetProperty("status", out var value))
                    {
                        conditions[type.GetString() ?? string.Empty] = value.GetString() ?? string.Empty;
                    }
                }
            }

            return new KedaScaledObject(name, uid, target, annotations, conditions);
        }

        public static IReadOnlyList<KedaScaledObject> ParseList(JsonElement list)
        {
            if (!list.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The ScaledObject list has no items array; which workloads are autoscaled is UNKNOWN.");
            }

            return items.EnumerateArray().Select(Parse).ToList();
        }
    }

    /// <summary>An HPA that targets a Deployment, reduced to what the activation checks need.</summary>
    internal sealed record HpaState(string? OwnerScaledObjectUid, bool ScalingActive);

    /// <summary>
    /// Decides when a KEDA-scaled Deployment is proven stopped or active. KEDA's HPA owns the replica
    /// count of such a Deployment, so it is stopped by pausing its ScaledObject at zero, never by
    /// writing spec.replicas, which the HPA would immediately overwrite.
    /// </summary>
    internal static class KedaWorkloadState
    {
        /// <summary>
        /// Paused at zero: KEDA acknowledged the pause and nothing is running. KEDA leaves an HPA behind
        /// that is disabled because its target has zero replicas, so only an HPA that is scaling counts.
        /// </summary>
        public static bool IsPausedAtZero(KedaScaledObject scaledObject, IReadOnlyList<HpaState> targetingHpas, int? deploymentReplicas, int podCount)
        {
            return scaledObject.Annotations.TryGetValue(KedaScaledObject.PausedReplicasAnnotation, out var pausedReplicas) &&
                   pausedReplicas == "0" &&
                   scaledObject.Condition("Paused") == "True" &&
                   !targetingHpas.Any(hpa => hpa.ScalingActive) &&
                   deploymentReplicas == 0 &&
                   podCount == 0;
        }

        /// <summary>
        /// Active: the pause is gone and acknowledged, exactly one HPA owned by this ScaledObject is
        /// scaling, and every desired replica of the current generation is ready.
        /// </summary>
        public static bool IsActive(
            KedaScaledObject scaledObject,
            IReadOnlyList<HpaState> targetingHpas,
            int? specReplicas,
            int? readyReplicas,
            long? observedGeneration,
            long? generation)
        {
            return !scaledObject.HasPauseRequest &&
                   scaledObject.Condition("Paused") == "False" &&
                   scaledObject.Condition("Ready") == "True" &&
                   targetingHpas.Count == 1 &&
                   targetingHpas[0].OwnerScaledObjectUid == scaledObject.Uid &&
                   !string.IsNullOrEmpty(scaledObject.Uid) &&
                   targetingHpas[0].ScalingActive &&
                   observedGeneration >= generation &&
                   specReplicas >= 1 &&
                   readyReplicas == specReplicas;
        }
    }
}

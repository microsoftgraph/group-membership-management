// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using k8s;
using k8s.Models;
using Services.WebApi;
using System.Text.Json;

namespace WebApi.Tests
{
    [TestClass]
    public class KedaWorkloadStateTests
    {
        private const string ScalerUid = "scaler-uid";

        private static KedaScaledObject ScaledObject(string? pausedReplicas = null, string? pausedCondition = "False", string? readyCondition = "True", string? pausedAnnotation = null)
        {
            var annotations = new Dictionary<string, string>();
            if (pausedReplicas != null) { annotations[KedaScaledObject.PausedReplicasAnnotation] = pausedReplicas; }
            if (pausedAnnotation != null) { annotations[KedaScaledObject.PausedAnnotation] = pausedAnnotation; }

            var conditions = new Dictionary<string, string>();
            if (pausedCondition != null) { conditions["Paused"] = pausedCondition; }
            if (readyCondition != null) { conditions["Ready"] = readyCondition; }

            return new KedaScaledObject("groupmembershipobtainer", ScalerUid, "groupmembershipobtainer", annotations, conditions);
        }

        private static readonly IReadOnlyList<HpaState> OwnedActiveHpa = [new HpaState(ScalerUid, true)];

        [TestMethod]
        public void ParseListReadsTargetAnnotationsAndConditions()
        {
            var json = JsonDocument.Parse("""
                {"items":[{
                  "metadata":{"name":"groupmembershipobtainer","uid":"scaler-uid","annotations":{"autoscaling.keda.sh/paused-replicas":"0"}},
                  "spec":{"scaleTargetRef":{"name":"groupmembershipobtainer"}},
                  "status":{"conditions":[{"type":"Ready","status":"True"},{"type":"Paused","status":"True"}]}
                }]}
                """).RootElement;

            var scaledObject = KedaScaledObject.ParseList(json).Single();

            Assert.AreEqual("groupmembershipobtainer", scaledObject.TargetName);
            Assert.AreEqual(ScalerUid, scaledObject.Uid);
            Assert.IsTrue(scaledObject.HasPauseRequest);
            Assert.AreEqual("True", scaledObject.Condition("Paused"));
            Assert.IsNull(scaledObject.Condition("Active"));
        }

        [TestMethod]
        public void ParseListWithoutItemsIsUnknownNotEmpty()
        {
            Assert.ThrowsException<InvalidOperationException>(() => KedaScaledObject.ParseList(JsonDocument.Parse("{}").RootElement));
        }

        [TestMethod]
        public void PausedAtZeroRequiresAcknowledgedPauseAndNothingRunning()
        {
            Assert.IsTrue(KedaWorkloadState.IsPausedAtZero(ScaledObject("0", "True"), [], 0, 0));
            Assert.IsTrue(KedaWorkloadState.IsPausedAtZero(ScaledObject("0", "True"), [new HpaState(ScalerUid, false)], 0, 0), "KEDA leaves a disabled HPA behind");

            Assert.IsFalse(KedaWorkloadState.IsPausedAtZero(ScaledObject("0", "False"), [], 0, 0), "KEDA has not acknowledged the pause");
            Assert.IsFalse(KedaWorkloadState.IsPausedAtZero(ScaledObject("2", "True"), [], 0, 0), "Paused at a nonzero count");
            Assert.IsFalse(KedaWorkloadState.IsPausedAtZero(ScaledObject(null, "True", pausedAnnotation: "true"), [], 0, 0), "Paused at the current count, not zero");
            Assert.IsFalse(KedaWorkloadState.IsPausedAtZero(ScaledObject("0", "True"), OwnedActiveHpa, 0, 0), "The HPA can still scale it up");
            Assert.IsFalse(KedaWorkloadState.IsPausedAtZero(ScaledObject("0", "True"), [], 3, 0), "Deployment still wants replicas");
            Assert.IsFalse(KedaWorkloadState.IsPausedAtZero(ScaledObject("0", "True"), [], null, 0), "Unknown replica count is not zero");
            Assert.IsFalse(KedaWorkloadState.IsPausedAtZero(ScaledObject("0", "True"), [], 0, 1), "A pod, including a terminating one, is still running");
        }

        [TestMethod]
        public void ActiveRequiresResumedOwnedScalingHpaAndAllDesiredReplicasReady()
        {
            Assert.IsTrue(KedaWorkloadState.IsActive(ScaledObject(), OwnedActiveHpa, 3, 3, 4, 4));
            Assert.IsTrue(KedaWorkloadState.IsActive(ScaledObject(pausedAnnotation: "false"), OwnedActiveHpa, 3, 3, 4, 4), "An explicit paused=false is how resume reports the unpause");

            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject("0", "False"), OwnedActiveHpa, 3, 3, 4, 4), "Pause request still present");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(pausedAnnotation: "true"), OwnedActiveHpa, 3, 3, 4, 4), "Pause request still present");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(pausedCondition: "True"), OwnedActiveHpa, 3, 3, 4, 4), "KEDA still reports Paused");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(pausedCondition: null), OwnedActiveHpa, 3, 3, 4, 4), "KEDA has not acknowledged the unpause");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(pausedCondition: "Unknown"), OwnedActiveHpa, 3, 3, 4, 4), "KEDA has not acknowledged the unpause");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(readyCondition: "False"), OwnedActiveHpa, 3, 3, 4, 4), "ScaledObject not ready");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(), [], 3, 3, 4, 4), "No HPA yet");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(), [new HpaState("other", true)], 3, 3, 4, 4), "HPA owned by something else");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(), [new HpaState(ScalerUid, false)], 3, 3, 4, 4), "HPA not scaling");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(), [new HpaState(ScalerUid, true), new HpaState(ScalerUid, true)], 3, 3, 4, 4), "Ambiguous HPAs");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(), OwnedActiveHpa, 3, 2, 4, 4), "Not every desired replica is ready");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(), OwnedActiveHpa, 0, 0, 4, 4), "Still at zero");
            Assert.IsFalse(KedaWorkloadState.IsActive(ScaledObject(), OwnedActiveHpa, 3, 3, 3, 4), "Status is from an older generation");
        }

        [TestMethod]
        public void PatchesSendExplicitNullsSoAnnotationsAreRemoved()
        {
            Assert.AreEqual(
                """{"metadata":{"annotations":{"autoscaling.keda.sh/paused-replicas":"0","autoscaling.keda.sh/paused":null}}}""",
                KubernetesJson.Serialize(KedaScaledObject.PauseAtZeroPatch()));
            Assert.AreEqual(
                """{"metadata":{"annotations":{"autoscaling.keda.sh/paused-replicas":null,"autoscaling.keda.sh/paused":"false"}}}""",
                KubernetesJson.Serialize(KedaScaledObject.ResumePatch()));
        }

        [TestMethod]
        public void AutoscaledStopBudgetUsesAcceptedDeploymentGracePlusMarginWithBoundedFallback()
        {
            static V1Deployment Deployment(long? grace) => new()
            {
                Spec = new V1DeploymentSpec
                {
                    Template = new V1PodTemplateSpec
                    {
                        Spec = new V1PodSpec
                        {
                            Containers = [new V1Container { Name = "worker" }],
                            TerminationGracePeriodSeconds = grace
                        }
                    }
                }
            };

            Assert.AreEqual(TimeSpan.FromSeconds(900), AksWorkloadService.GetAutoscaledStopTimeout(Deployment(660)));
            Assert.AreEqual(TimeSpan.FromSeconds(300), AksWorkloadService.GetAutoscaledStopTimeout(Deployment(null)));
            Assert.AreEqual(TimeSpan.FromSeconds(1800), AksWorkloadService.GetAutoscaledStopTimeout(Deployment(1560)));
            Assert.AreEqual(TimeSpan.FromSeconds(300), AksWorkloadService.GetAutoscaledStopTimeout(Deployment(1561)));
            Assert.AreEqual(TimeSpan.FromSeconds(300), AksWorkloadService.GetAutoscaledStopTimeout(Deployment(long.MaxValue)));
        }

        [TestMethod]
        public void LivePodsIgnoresPodsTheKubeletAlreadyKilled()
        {
            static V1Pod Pod(string name, string phase) => new() { Metadata = new V1ObjectMeta { Name = name }, Status = new V1PodStatus { Phase = phase } };

            var pods = new V1PodList { Items = [Pod("running", "Running"), Pod("pending", "Pending"), Pod("preempted", "Failed"), Pod("done", "Succeeded"), new V1Pod { Metadata = new V1ObjectMeta { Name = "no-status" } }] };

            CollectionAssert.AreEqual(new[] { "running", "pending", "no-status" }, AksWorkloadService.LivePods(pods).Select(pod => pod.Metadata.Name).ToArray());
        }
    }
}

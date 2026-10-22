{{/*
The ScaledObject of a KEDA-autoscaled service, named after the release and scaling the
Deployment of the same name. The service supplies, in values.autoscaling:
  pollingInterval, behavior   - required; behavior is the HPA behavior map
  durableQueues               - optional; expands to one azure-queue trigger per Durable
                                work-item queue and control queue of the service's task hub
  triggers                    - optional; extra triggers, rendered with tpl so they can
                                reference other values
At least one of durableQueues and triggers is required.
*/}}
{{- define "gmm.keda.scaledObject" -}}
{{- include "gmm.deployments.autoscaledValidate" . -}}
{{- $a := .Values.autoscaling -}}
{{- if not (or $a.durableQueues $a.triggers) -}}{{- fail "autoscaling needs durableQueues or triggers" -}}{{- end -}}
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: {{ include "gmm.names.resource" . }}
  labels:
    {{- include "gmm.names.labels" . | nindent 4 }}
  {{- if and (hasKey $a "pausedReplicas") (ne $a.pausedReplicas nil) }}
  annotations:
    autoscaling.keda.sh/paused-replicas: {{ $a.pausedReplicas | quote }}
  {{- end }}
spec:
  scaleTargetRef:
    name: {{ include "gmm.names.resource" . }}
  pollingInterval: {{ required "autoscaling.pollingInterval is required" $a.pollingInterval }}
  minReplicaCount: {{ $a.minReplicaCount }}
  maxReplicaCount: {{ $a.maxReplicaCount }}
  advanced:
    restoreToOriginalReplicaCount: false
    horizontalPodAutoscalerConfig:
      name: keda-hpa-{{ include "gmm.names.resource" . }}
      behavior:
        {{- toYaml (required "autoscaling.behavior is required" $a.behavior) | nindent 8 }}
  triggers:
    {{- with $a.triggers }}
    {{- tpl (toYaml .) $ | nindent 4 }}
    {{- end }}
    {{- include "gmm.keda.durableTriggers" . | nindent 4 }}
{{- end -}}

{{- define "gmm.keda.durableTriggers" -}}
{{- with .Values.autoscaling.durableQueues -}}
{{- $hub := include "gmm.names.durableHubName" $ | lower -}}
{{- $queues := list "workitems" -}}
{{- range $i := until (int .controlQueueCount) -}}
{{- $queues = append $queues (printf "control-%02d" $i) -}}
{{- end -}}
{{- range $queue := $queues }}
- type: azure-queue
  name: durable-{{ $queue }}
  metricType: AverageValue
  useCachedMetrics: false
  metadata:
    accountName: {{ required "storage.accountName is required" $.Values.storage.accountName | quote }}
    queueName: {{ printf "%s-%s" $hub $queue | quote }}
    queueLength: {{ if eq $queue "workitems" }}{{ $.Values.autoscaling.durableQueues.workitems | quote }}{{ else }}{{ $.Values.autoscaling.durableQueues.control | quote }}{{ end }}
    queueLengthStrategy: all
  authenticationRef:
    name: {{ include "gmm.names.resource" $ }}-metrics
{{- end }}
{{- end -}}
{{- end -}}

{{/* Lets the KEDA operator read the service's metrics as the service's own workload identity. */}}
{{- define "gmm.keda.triggerAuthentication" -}}
{{- include "gmm.deployments.autoscaledValidate" . -}}
apiVersion: keda.sh/v1alpha1
kind: TriggerAuthentication
metadata:
  name: {{ include "gmm.names.resource" . }}-metrics
  labels:
    {{- include "gmm.names.labels" . | nindent 4 }}
spec:
  podIdentity:
    provider: azure-workload
    identityId: {{ .Values.workloadIdentity.clientId | quote }}
    identityTenantId: {{ .Values.keyVault.tenantId | quote }}
{{- end -}}

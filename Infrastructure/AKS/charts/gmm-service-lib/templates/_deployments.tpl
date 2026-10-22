{{/*
The Deployment of a service. A chart's templates/deployment.yaml includes exactly one entry point, chosen
to match its gmm.microsoft.com/scaling annotation; the other one fails to render:

  gmm.deployments.fixed       scaling: fixed  (JobTrigger)
  gmm.deployments.autoscaled  scaling: keda   (GroupMembershipObtainer)

The two halves below are independent. Read only the one that matches your service.
*/}}

{{/* ==================================================================================================
     FIXED SERVICE (gmm.microsoft.com/scaling: fixed), such as JobTrigger.
     Helm owns the replica count, which for a singleton or timer-triggered service is a strict 0/1 fence:
     the count IS the active/standby switch.
     ================================================================================================== */}}

{{- define "gmm.deployments.fixedReplicas" -}}
{{- if ne (include "gmm.names.scaling" .) "fixed" -}}
{{- fail "This chart's gmm.microsoft.com/scaling annotation is keda, so its Deployment must use gmm.deployments.autoscaled" -}}
{{- end -}}
{{- if hasKey .Values "autoscaling" -}}
{{- fail "autoscaling values require the gmm.microsoft.com/scaling: keda chart annotation" -}}
{{- end -}}
{{- if or .Values.workload.timerTriggered .Values.workload.singleton -}}
{{- if not (regexMatch "^[01]$" (toString .Values.replicaCount)) -}}
{{- fail (printf "replicaCount must be 0 or 1 for the singleton or timer-triggered service %s: the replica count IS the active/standby fence." (include "gmm.names.service" .)) -}}
{{- end -}}
{{- end -}}
{{- .Values.replicaCount | int -}}
{{- end -}}

{{- define "gmm.deployments.fixed" -}}
{{- /* Validates the annotations the deployment scripts resolve aksServices against. */ -}}
{{- $_ := include "gmm.names.service" . -}}
{{- $replicas := include "gmm.deployments.fixedReplicas" . -}}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "gmm.names.resource" . }}
  namespace: {{ .Release.Namespace }}
  labels:
    {{- include "gmm.names.labels" . | nindent 4 }}
  annotations:
    gmm.microsoft.com/cluster-role: {{ if gt (int $replicas) 0 }}active{{ else }}standby{{ end }}
spec:
  replicas: {{ $replicas }}
  # A Durable singleton must never have two hosts alive at once.
  strategy:
    type: Recreate
  revisionHistoryLimit: {{ .Values.revisionHistoryLimit | default 3 }}
  selector:
    matchLabels:
      {{- include "gmm.names.selectorLabels" . | nindent 6 }}
  template:
    {{- include "gmm.podTemplate" (dict "root" . "optionalSecrets" false) | nindent 4 }}
{{- end -}}

{{/* ==================================================================================================
     AUTOSCALED SERVICE (gmm.microsoft.com/scaling: keda), such as GroupMembershipObtainer.
     While it is active its replica count belongs to KEDA's HPA, so the Deployment omits `replicas`. A
     maintenance pause hands the count back to Helm. The chart also includes gmm.keda.scaledObject and
     gmm.keda.triggerAuthentication (see _keda.tpl).
     ================================================================================================== */}}

{{- define "gmm.deployments.autoscaledValidate" -}}
{{- $a := required "autoscaling configuration is mandatory" .Values.autoscaling -}}
{{- if hasKey $a "enabled" -}}{{- fail "autoscaling.enabled is not supported; pause the workload for maintenance" -}}{{- end -}}
{{- if or .Values.workload.timerTriggered .Values.workload.singleton -}}{{- fail "An autoscaled service cannot be a timer or singleton workload" -}}{{- end -}}
{{- range $key := list "minReplicaCount" "maxReplicaCount" -}}
{{- if not (regexMatch "^[1-9][0-9]*$" (toString (get $a $key))) -}}{{- fail (printf "autoscaling.%s must be a positive integer" $key) -}}{{- end -}}
{{- end -}}
{{- if gt (int $a.minReplicaCount) (int $a.maxReplicaCount) -}}{{- fail "autoscaling replica bounds are inverted" -}}{{- end -}}
{{- if and (hasKey $a "pausedReplicas") (ne $a.pausedReplicas nil) -}}
{{- if or (not (regexMatch "^(0|[1-9][0-9]*)$" (toString $a.pausedReplicas))) (gt (int $a.pausedReplicas) (int $a.maxReplicaCount)) -}}
{{- fail "autoscaling.pausedReplicas must be an integer from zero through maxReplicaCount" -}}
{{- end -}}
{{- end -}}
{{- if not (regexMatch "^([1-9][0-9]{0,2}|1[0-4][0-9]{2}|15[0-5][0-9]|1560)$" (toString (required "terminationGracePeriodSeconds is required for an autoscaled service" .Values.terminationGracePeriodSeconds))) -}}
{{- fail "terminationGracePeriodSeconds must be an integer from 1 to 1560" -}}
{{- end -}}
{{- end -}}

{{/*
The replica count Helm should write while the service is paused, or an empty string while it is active.
Empty means "write no `replicas`", leaving the count to the HPA.
*/}}
{{- define "gmm.deployments.autoscaledPausedReplicas" -}}
{{- if ne (include "gmm.names.scaling" .) "keda" -}}
{{- fail "This chart's gmm.microsoft.com/scaling annotation is fixed, so its Deployment must use gmm.deployments.fixed" -}}
{{- end -}}
{{- include "gmm.deployments.autoscaledValidate" . -}}
{{- if and (hasKey .Values.autoscaling "pausedReplicas") (ne .Values.autoscaling.pausedReplicas nil) -}}
{{- .Values.autoscaling.pausedReplicas -}}
{{- end -}}
{{- end -}}

{{- define "gmm.deployments.autoscaled" -}}
{{- /* Validates the annotations the deployment scripts resolve aksServices against. */ -}}
{{- $_ := include "gmm.names.service" . -}}
{{- $paused := include "gmm.deployments.autoscaledPausedReplicas" . -}}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "gmm.names.resource" . }}
  namespace: {{ .Release.Namespace }}
  labels:
    {{- include "gmm.names.labels" . | nindent 4 }}
  annotations:
    gmm.microsoft.com/cluster-role: {{ if or (eq $paused "") (gt (int $paused) 0) }}active{{ else }}standby{{ end }}
spec:
  # Omitted while the service is active, so Helm never contends with its HPA.
  {{- if ne $paused "" }}
  replicas: {{ $paused }}
  {{- end }}
  # A Durable singleton must never have two hosts alive at once.
  strategy:
    type: Recreate
  revisionHistoryLimit: {{ .Values.revisionHistoryLimit | default 3 }}
  selector:
    matchLabels:
      {{- include "gmm.names.selectorLabels" . | nindent 6 }}
  template:
    {{- include "gmm.podTemplate" (dict "root" . "optionalSecrets" true) | nindent 4 }}
{{- end -}}

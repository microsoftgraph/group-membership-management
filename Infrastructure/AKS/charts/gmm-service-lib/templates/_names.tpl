{{/*
Names and labels. Every Kubernetes name a service owns is derived here, so the objects always agree.
*/}}

{{- define "gmm.names.resource" -}}
{{- $name := default .Chart.Name .Values.service.name | lower -}}
{{- if gt (len $name) 63 -}}
{{- fail (printf "service.name %q is %d characters once lowercased, which exceeds the 63-character limit for the app.kubernetes.io/name label value. This value is NOT truncated, because a truncated name can collide with another service's. Choose a shorter service.name." $name (len $name)) -}}
{{- end -}}
{{- if not (regexMatch "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$" $name) -}}
{{- fail (printf "service.name %q is not a valid Kubernetes name once lowercased: it must contain only lowercase letters, digits and hyphens, and must start and end with a letter or digit." $name) -}}
{{- end -}}
{{- $name -}}
{{- end -}}

{{/*
The service's GMM name in PascalCase (e.g. JobTrigger), from the chart's
gmm.microsoft.com/service annotation - the value Get-AksServiceChart matches against
the aksServices deployment parameter.
*/}}
{{- define "gmm.names.service" -}}
{{- $name := index (default dict .Chart.Annotations) "gmm.microsoft.com/service" -}}
{{- if not $name -}}
{{- fail "Chart.yaml must set the gmm.microsoft.com/service annotation (PascalCase GMM service name, e.g. JobTrigger)" -}}
{{- end -}}
{{- if ne (lower $name) .Chart.Name -}}
{{- fail (printf "gmm.microsoft.com/service %q must match the chart name %q case-insensitively" $name .Chart.Name) -}}
{{- end -}}
{{- $name -}}
{{- end -}}

{{/*
How the service scales: "fixed" (gmm.deployments.fixed) or "keda" (gmm.deployments.autoscaled). The
deploy scripts read the same annotation, so the chart and the scripts cannot disagree.
*/}}
{{- define "gmm.names.scaling" -}}
{{- $scaling := index (default dict .Chart.Annotations) "gmm.microsoft.com/scaling" -}}
{{- if not (has $scaling (list "keda" "fixed")) -}}
{{- fail "Chart.yaml must set the gmm.microsoft.com/scaling annotation to keda or fixed" -}}
{{- end -}}
{{- $scaling -}}
{{- end -}}

{{/* Not overridable: the federated identity credential's subject embeds this name. */}}
{{- define "gmm.names.serviceAccountName" -}}
{{- printf "%s-sa" (include "gmm.names.resource" .) -}}
{{- end -}}

{{- define "gmm.names.configMapName" -}}
{{- printf "%s-config" (include "gmm.names.resource" .) -}}
{{- end -}}

{{/* The Durable Functions task hub name, which isolates one host's orchestration state from another's. */}}
{{- define "gmm.names.durableHubName" -}}
{{- $raw := required "durable.hubName is required and must be unique per host" .Values.durable.hubName -}}
{{- $hub := $raw | replace "-" "" -}}
{{- if gt (len $hub) 45 -}}
{{- fail (printf "durable.hubName %q becomes %q (%d characters) once hyphens are removed, which exceeds the Durable Functions task hub limit of 45. This value is NOT truncated, because a truncated hub can collide with another host's and put two hosts on the same orchestration and lease rows. Choose a shorter durable.hubName." $raw $hub (len $hub)) -}}
{{- end -}}
{{- if lt (len $hub) 3 -}}
{{- fail (printf "durable.hubName %q becomes %q (%d characters) once hyphens are removed; Durable Functions requires at least 3." $raw $hub (len $hub)) -}}
{{- end -}}
{{- if not (regexMatch "^[A-Za-z][A-Za-z0-9]*$" $hub) -}}
{{- fail (printf "durable.hubName %q becomes %q once hyphens are removed, which is not a valid Durable Functions task hub name: it must start with a letter and contain only letters and digits." $raw $hub) -}}
{{- end -}}
{{- $hub -}}
{{- end -}}

{{/* Supersets gmm.names.selectorLabels; never emit both in one label block, as strict
     decoders reject the duplicate keys. */}}
{{- define "gmm.names.labels" -}}
{{ include "gmm.names.selectorLabels" . }}
app.kubernetes.io/part-of: gmm
app.kubernetes.io/managed-by: {{ .Release.Service }}
gmm.microsoft.com/service: {{ include "gmm.names.resource" . }}
{{- end -}}

{{- define "gmm.names.selectorLabels" -}}
app.kubernetes.io/name: {{ include "gmm.names.resource" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

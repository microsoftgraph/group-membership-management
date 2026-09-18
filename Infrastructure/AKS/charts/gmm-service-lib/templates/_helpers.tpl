{{- define "gmm.name" -}}
{{- $name := default .Chart.Name .Values.service.name | lower -}}
{{- if gt (len $name) 63 -}}
{{- fail (printf "service.name %q is %d characters once lowercased, which exceeds the 63-character limit for the app.kubernetes.io/name label value. This value is NOT truncated, because a truncated name can collide with another service's. Choose a shorter service.name." $name (len $name)) -}}
{{- end -}}
{{- if not (regexMatch "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$" $name) -}}
{{- fail (printf "service.name %q is not a valid Kubernetes name once lowercased: it must contain only lowercase letters, digits and hyphens, and must start and end with a letter or digit." $name) -}}
{{- end -}}
{{- $name -}}
{{- end -}}

{{/* Not overridable: the federated identity credential's subject embeds this name. */}}
{{- define "gmm.serviceAccountName" -}}
{{- printf "%s-sa" (include "gmm.name" .) -}}
{{- end -}}

{{- define "gmm.configMapName" -}}
{{- printf "%s-config" (include "gmm.name" .) -}}
{{- end -}}

{{/* Supersets gmm.selectorLabels; never emit both in one label block, as strict
     decoders reject the duplicate keys. */}}
{{- define "gmm.labels" -}}
{{ include "gmm.selectorLabels" . }}
app.kubernetes.io/part-of: gmm
app.kubernetes.io/managed-by: {{ .Release.Service }}
gmm.microsoft.com/service: {{ include "gmm.name" . }}
{{- end -}}

{{- define "gmm.selectorLabels" -}}
app.kubernetes.io/name: {{ include "gmm.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "gmm.image" -}}
{{- $repo := required "image.repository is required" .Values.image.repository -}}
{{- if .Values.image.digest -}}
{{- if not (hasPrefix "sha256:" .Values.image.digest) -}}
{{- fail (printf "image.digest must be a sha256 digest, got %q" .Values.image.digest) -}}
{{- end -}}
{{- printf "%s@%s" $repo .Values.image.digest -}}
{{- else if and .Values.image.tag .Values.image.allowTag -}}
{{- printf "%s:%s" $repo .Values.image.tag -}}
{{- else -}}
{{- fail "image.digest is required: images are referenced by digest, not tag. Set image.allowTag=true only for local `helm template` inspection." -}}
{{- end -}}
{{- end -}}

{{- define "gmm.replicas" -}}
{{- $r := .Values.replicaCount | int -}}
{{- if .Values.workload.timerTriggered -}}
{{- if or (lt $r 0) (gt $r 1) -}}
{{- fail (printf "replicaCount must be 0 or 1 for the timer-triggered service %s: the replica count IS the active/standby fence." (include "gmm.name" .)) -}}
{{- end -}}
{{- end -}}
{{- $r -}}
{{- end -}}

{{- define "gmm.durable.hubName" -}}
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

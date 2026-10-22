{{/*
The pod template every service shares: identity, security context, image, environment from the ConfigMap
and the synced Key Vault Secrets, the CSI mounts that populate those Secrets, resources and probes.

Called with a dict: `root` is the chart context, and `optionalSecrets` says whether the Key Vault
Secrets are optional. A fixed service's Secrets are required, so a missing one fails the pod rather than
starting the host with no connection strings. An autoscaled service's Secrets only exist once a pod has
mounted the CSI volume, yet KEDA reads them before it creates the HPA that starts the first pod, so
there they are optional.
*/}}
{{- define "gmm.podTemplate" -}}
{{- $root := .root -}}
metadata:
  labels:
    {{- include "gmm.names.labels" $root | nindent 4 }}
    azure.workload.identity/use: "true"
  annotations:
    checksum/config: {{ include "gmm.configMap" $root | sha256sum }}
spec:
  nodeSelector:
    kubernetes.azure.com/agentpool: apppool
  serviceAccountName: {{ include "gmm.names.serviceAccountName" $root }}
  automountServiceAccountToken: false
  terminationGracePeriodSeconds: {{ $root.Values.terminationGracePeriodSeconds | default 60 }}
  securityContext:
    # The Functions base image runs as root; it ships `app`, uid 1654 (not 1000).
    runAsNonRoot: {{ $root.Values.securityContext.runAsNonRoot | default true }}
    runAsUser: {{ $root.Values.securityContext.runAsUser | default 1654 }}
    runAsGroup: {{ $root.Values.securityContext.runAsGroup | default 1654 }}
    fsGroup: {{ $root.Values.securityContext.fsGroup | default 1654 }}
    seccompProfile:
      type: RuntimeDefault
  containers:
    - name: {{ include "gmm.names.resource" $root }}
      image: {{ include "gmm.podTemplate.image" $root | quote }}
      imagePullPolicy: {{ $root.Values.image.pullPolicy | default "IfNotPresent" }}
      securityContext:
        allowPrivilegeEscalation: false
        privileged: false
        readOnlyRootFilesystem: {{ $root.Values.securityContext.readOnlyRootFilesystem | default false }}
        capabilities:
          drop: ["ALL"]
      envFrom:
        - configMapRef:
            name: {{ include "gmm.names.configMapName" $root }}
        {{- range $vault := $root.Values.keyVault.vaults }}
        - secretRef:
            name: {{ printf "%s-kv-%s" (include "gmm.names.resource" $root) $vault.alias }}
            {{- if $.optionalSecrets }}
            optional: true
            {{- end }}
        {{- end }}
      volumeMounts:
        {{- range $vault := $root.Values.keyVault.vaults }}
        # The CSI volume must be mounted or the synced Secret above is never
        # populated.
        - name: {{ printf "kv-%s" $vault.alias }}
          mountPath: {{ printf "/mnt/secrets/%s" $vault.alias }}
          readOnly: true
        {{- end }}
      resources:
        {{- toYaml $root.Values.resources | nindent 8 }}
      {{- with $root.Values.probes }}
      {{- if .enabled }}
      {{- with .liveness }}
      livenessProbe:
        {{- toYaml . | nindent 8 }}
      {{- end }}
      {{- with .readiness }}
      readinessProbe:
        {{- toYaml . | nindent 8 }}
      {{- end }}
      {{- end }}
      {{- end }}
  volumes:
    {{- range $vault := $root.Values.keyVault.vaults }}
    - name: {{ printf "kv-%s" $vault.alias }}
      csi:
        driver: secrets-store.csi.k8s.io
        readOnly: true
        volumeAttributes:
          secretProviderClass: {{ printf "%s-kv-%s" (include "gmm.names.resource" $root) $vault.alias }}
    {{- end }}
{{- end -}}

{{/* The container image: always referenced by digest. */}}
{{- define "gmm.podTemplate.image" -}}
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

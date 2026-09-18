{{- define "gmm.deployment" -}}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "gmm.name" . }}
  namespace: {{ .Release.Namespace }}
  labels:
    {{- include "gmm.labels" . | nindent 4 }}
  annotations:
    gmm.microsoft.com/cluster-role: {{ if eq (include "gmm.replicas" . | int) 1 }}active{{ else }}standby{{ end }}
spec:
  replicas: {{ include "gmm.replicas" . }}
  # A Durable singleton must never have two hosts alive at once.
  strategy:
    type: Recreate
  revisionHistoryLimit: {{ .Values.revisionHistoryLimit | default 3 }}
  selector:
    matchLabels:
      {{- include "gmm.selectorLabels" . | nindent 6 }}
  template:
    metadata:
      labels:
        {{- include "gmm.labels" . | nindent 8 }}
        azure.workload.identity/use: "true"
      annotations:
        checksum/config: {{ include "gmm.configMap" . | sha256sum }}
    spec:
      serviceAccountName: {{ include "gmm.serviceAccountName" . }}
      automountServiceAccountToken: false
      terminationGracePeriodSeconds: {{ .Values.terminationGracePeriodSeconds | default 60 }}
      securityContext:
        # The Functions base image runs as root; it ships `app`, uid 1654 (not 1000).
        runAsNonRoot: {{ .Values.securityContext.runAsNonRoot | default true }}
        runAsUser: {{ .Values.securityContext.runAsUser | default 1654 }}
        runAsGroup: {{ .Values.securityContext.runAsGroup | default 1654 }}
        fsGroup: {{ .Values.securityContext.fsGroup | default 1654 }}
        seccompProfile:
          type: RuntimeDefault
      containers:
        - name: {{ include "gmm.name" . }}
          image: {{ include "gmm.image" . | quote }}
          imagePullPolicy: {{ .Values.image.pullPolicy | default "IfNotPresent" }}
          securityContext:
            allowPrivilegeEscalation: false
            privileged: false
            readOnlyRootFilesystem: {{ .Values.securityContext.readOnlyRootFilesystem | default false }}
            capabilities:
              drop: ["ALL"]
          envFrom:
            - configMapRef:
                name: {{ include "gmm.configMapName" . }}
            {{- range $vault := .Values.keyVault.vaults }}
            # Deliberately not `optional: true`: a missing Secret must fail the pod,
            # not start the host with no connection strings.
            - secretRef:
                name: {{ printf "%s-kv-%s" (include "gmm.name" $) $vault.alias }}
            {{- end }}
          volumeMounts:
            {{- range $vault := .Values.keyVault.vaults }}
            # The CSI volume must be mounted or the synced Secret above is never
            # populated.
            - name: {{ printf "kv-%s" $vault.alias }}
              mountPath: {{ printf "/mnt/secrets/%s" $vault.alias }}
              readOnly: true
            {{- end }}
          resources:
            {{- toYaml .Values.resources | nindent 12 }}
          {{- with .Values.probes }}
          {{- if .enabled }}
          {{- with .liveness }}
          livenessProbe:
            {{- toYaml . | nindent 12 }}
          {{- end }}
          {{- with .readiness }}
          readinessProbe:
            {{- toYaml . | nindent 12 }}
          {{- end }}
          {{- end }}
          {{- end }}
      volumes:
        {{- range $vault := .Values.keyVault.vaults }}
        - name: {{ printf "kv-%s" $vault.alias }}
          csi:
            driver: secrets-store.csi.k8s.io
            readOnly: true
            volumeAttributes:
              secretProviderClass: {{ printf "%s-kv-%s" (include "gmm.name" $) $vault.alias }}
        {{- end }}
{{- end -}}

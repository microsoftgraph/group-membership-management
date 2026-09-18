{{- define "gmm.serviceAccount" -}}
apiVersion: v1
kind: ServiceAccount
metadata:
  name: {{ include "gmm.serviceAccountName" . }}
  namespace: {{ .Release.Namespace }}
  labels:
    {{- include "gmm.labels" . | nindent 4 }}
  annotations:
    azure.workload.identity/client-id: {{ required "workloadIdentity.clientId is required — the client id of this service's user-assigned managed identity" .Values.workloadIdentity.clientId | quote }}
automountServiceAccountToken: false
{{- end -}}

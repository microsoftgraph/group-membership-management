{{- define "gmm.configMap" -}}
apiVersion: v1
kind: ConfigMap
metadata:
  name: {{ include "gmm.configMapName" . }}
  namespace: {{ .Release.Namespace }}
  labels:
    {{- include "gmm.labels" . | nindent 4 }}
data:
  FUNCTIONS_WORKER_RUNTIME: "dotnet-isolated"
  FUNCTIONS_EXTENSION_VERSION: "~4"
  AzureFunctionsJobHost__logging__console__isEnabled: "true"
  WEBSITE_HOSTNAME: "localhost"
  WEBSITE_SITE_NAME: {{ include "gmm.name" . | quote }}
  # Port 80 is privileged; the host cannot bind it as a non-root uid.
  ASPNETCORE_URLS: {{ .Values.config.httpPort | default 8080 | printf "http://+:%v" | quote }}

  AzureFunctionsJobHost__extensions__durableTask__hubName: {{ include "gmm.durable.hubName" . | quote }}
  AzureFunctionsWebHost__hostid: {{ required "durable.hostId is required and must be unique per host" .Values.durable.hostId | quote }}

  AzureWebJobsStorage__accountName: {{ required "storage.accountName is required" .Values.storage.accountName | quote }}
  AzureWebJobsStorage__blobServiceUri: {{ printf "https://%s.blob.core.windows.net" .Values.storage.accountName | quote }}
  AzureWebJobsStorage__queueServiceUri: {{ printf "https://%s.queue.core.windows.net" .Values.storage.accountName | quote }}
  AzureWebJobsStorage__tableServiceUri: {{ printf "https://%s.table.core.windows.net" .Values.storage.accountName | quote }}
  AzureWebJobsStorage__credential: "managedidentity"
  AzureWebJobsStorage__clientId: {{ .Values.workloadIdentity.clientId | quote }}

  # Only WorkloadIdentityCredential consumes the projected federated token.
  AZURE_TOKEN_CREDENTIALS: {{ .Values.config.azureTokenCredentials | default "WorkloadIdentityCredential" | quote }}

  appConfigurationEndpoint: {{ required "config.appConfigurationEndpoint is required" .Values.config.appConfigurationEndpoint | quote }}

  {{- with .Values.config.bindingExpressionSettings }}

  {{- range $k, $v := . }}
  {{ $k }}: {{ $v | quote }}
  {{- end }}
  {{- end }}
{{- end -}}

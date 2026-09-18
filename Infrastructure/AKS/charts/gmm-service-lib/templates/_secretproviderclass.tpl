{{- define "gmm.secretProviderClasses" -}}
{{- $root := . -}}
{{- range $vault := .Values.keyVault.vaults }}
---
apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: {{ printf "%s-kv-%s" (include "gmm.name" $root) $vault.alias }}
  namespace: {{ $root.Release.Namespace }}
  labels:
    {{- include "gmm.labels" $root | nindent 4 }}
spec:
  provider: azure
  secretObjects:
    - secretName: {{ printf "%s-kv-%s" (include "gmm.name" $root) $vault.alias }}
      type: Opaque
      data:
        {{- range $vault.secrets }}
        - objectName: {{ .objectName | quote }}
          key: {{ .key | quote }}
        {{- end }}
  parameters:
    usePodIdentity: "false"
    useVMManagedIdentity: "false"
    clientID: {{ required "workloadIdentity.clientId is required" $root.Values.workloadIdentity.clientId | quote }}
    # From the keyVault.names map: `--set` replaces a list element instead of
    # merging, so setting a name on the list entry would discard its secrets.
    keyvaultName: {{ required (printf "keyVault.names.%s is required" $vault.alias) (index $root.Values.keyVault.names $vault.alias) | quote }}
    tenantId: {{ required "keyVault.tenantId is required" $root.Values.keyVault.tenantId | quote }}
    objects: |
      array:
        {{- range $vault.secrets }}
        - |
          objectName: {{ .objectName }}
          objectType: secret
        {{- end }}
{{- end }}
{{- end -}}

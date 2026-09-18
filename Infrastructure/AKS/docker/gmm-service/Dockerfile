ARG RUNTIME_IMAGE=mcr.microsoft.com/azure-functions/dotnet-isolated@sha256:2139b5ac3ee3e30caec5a6e0f8d38a432caadbdcf358cd8481d13a6f64f80290

FROM ${RUNTIME_IMAGE}

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

ENV AZURE_TOKEN_CREDENTIALS=WorkloadIdentityCredential

# Server GC assumes it owns the machine, which risks an OOMKill in a pod with a
# memory limit.
ENV DOTNET_gcServer=0

COPY . /home/site/wwwroot

RUN set -eu; \
    cd /home/site/wwwroot; \
    for f in host.json worker.config.json functions.metadata; do \
      [ -f "$f" ] || { echo "FATAL: $f missing from the function package"; exit 1; }; \
    done; \
    n="$(grep -c '"entryPoint":' functions.metadata || true)"; \
    [ "${n:-0}" -gt 0 ] || { echo "FATAL: functions.metadata declares 0 functions"; exit 1; }; \
    echo "function package OK: ${n} function(s) declared"

# TFM / base-runtime agreement check - do not remove. The mismatch is asymmetric:
# the 8.0 base ships both .NET 10 and .NET 8, while the 10.0 base ships only
# .NET 10, so a net8.0 package on the 10.0 base dies at startup rather than
# rolling forward. Failing here turns that CrashLoopBackOff into a build error.
RUN set -eu; \
    cd /home/site/wwwroot; \
    rc="$(ls *.runtimeconfig.json 2>/dev/null | head -n1 || true)"; \
    [ -n "$rc" ] || { echo "FATAL: no *.runtimeconfig.json in the package"; exit 1; }; \
    tfm="$(grep -o '"tfm"[[:space:]]*:[[:space:]]*"[^"]*"' "$rc" | sed 's/.*"\([^"]*\)"$/\1/')"; \
    major="$(echo "$tfm" | sed 's/^net\([0-9]*\)\..*/\1/')"; \
    echo "package targets ${tfm} (major ${major}), per ${rc}"; \
    if ! ls -d /usr/share/dotnet/shared/Microsoft.NETCore.App/${major}.* >/dev/null 2>&1; then \
      echo "FATAL: package targets ${tfm}, but this base image ships no .NET ${major} runtime."; \
      echo "       available: $(ls /usr/share/dotnet/shared/Microsoft.NETCore.App/ | tr '\n' ' ')"; \
      echo "       Either bump this environment's public-repo tag pin to a matching"; \
      echo "       .NET version, or change the base image digest to match the package."; \
      exit 1; \
    fi; \
    echo "base image provides a matching .NET ${major} runtime"

# Syntax reference: https://docs.docker.com/reference/dockerfile/
# AuditSphere worker image. One image serves every worker group (general, processing, records,
# mail, pbc, client-sites): Worker:Group configuration decides the capability composition at
# startup, and privileged Microsoft 365 credentials are only ever mounted to their isolated
# external-effect workers. Build context is the repository root.
#   docker build -f src/AuditSphereOps.Worker/Dockerfile -t auditsphere-worker:<git-sha> .
FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS build
ARG USE_PREBUILT_RELEASE=false
ARG WORKER_RELEASE_BUILD_SHA256=
ARG DOTNET_ROLL_FORWARD=disable
ENV DOTNET_ROLL_FORWARD=$DOTNET_ROLL_FORWARD
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props AuditSphereOps.slnx ./
COPY src/AuditSphereOps.Domain/AuditSphereOps.Domain.csproj src/AuditSphereOps.Domain/packages.lock.json src/AuditSphereOps.Domain/
COPY src/AuditSphereOps.Application/AuditSphereOps.Application.csproj src/AuditSphereOps.Application/packages.lock.json src/AuditSphereOps.Application/
COPY src/AuditSphereOps.Infrastructure/AuditSphereOps.Infrastructure.csproj src/AuditSphereOps.Infrastructure/packages.lock.json src/AuditSphereOps.Infrastructure/
COPY src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj src/AuditSphereOps.Worker/packages.lock.json src/AuditSphereOps.Worker/
RUN dotnet restore src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj --locked-mode
COPY src/ src/
COPY release-assets/worker-release-build/ /prebuilt-release/
RUN if [ "$USE_PREBUILT_RELEASE" = "true" ]; then \
      printf '%s' "$WORKER_RELEASE_BUILD_SHA256" | grep -Eq '^[0-9a-f]{64}$' && \
      printf '%s  %s\n' "$WORKER_RELEASE_BUILD_SHA256" /prebuilt-release/worker-release-build.tar.gz \
        | sha256sum --check --status - && \
      tar -xzf /prebuilt-release/worker-release-build.tar.gz --no-same-owner -C /repo; \
    elif [ "$USE_PREBUILT_RELEASE" != "false" ]; then \
      echo "USE_PREBUILT_RELEASE must be true or false" >&2; exit 1; \
    fi
RUN if [ "$USE_PREBUILT_RELEASE" = "true" ]; then \
      dotnet publish src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj \
        --configuration Release --no-restore --no-build -m:1 -p:BuildInParallel=false \
        -p:UseSharedCompilation=false -o /out -p:DebugType=None -p:DebugSymbols=false; \
    else \
      DOTNET_gcServer=0 DOTNET_GCConserveMemory=9 \
        dotnet publish src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj \
          --configuration Release --no-restore -m:1 -p:BuildInParallel=false \
          -p:UseSharedCompilation=false -o /out \
          -p:CscEnvironment=DOTNET_PROCESSOR_COUNT=1 \
          -p:RunAnalyzers=false -p:DebugType=None -p:DebugSymbols=false; \
    fi \
  && test -f /out/AuditSphereOps.Worker.dll

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /out/ .
RUN apt-get update \
  && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
  && rm -rf /var/lib/apt/lists/* \
  && mkdir -p /var/lib/auditsphere/pbc-staging /var/lib/auditsphere/release-checkpoints \
  && chown -R $APP_UID:$APP_UID /var/lib/auditsphere
ENV DOTNET_gcServer=1
USER $APP_UID
ENTRYPOINT ["dotnet", "AuditSphereOps.Worker.dll"]

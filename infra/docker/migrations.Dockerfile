# Migration bundle image for the dedicated migration deployment step. The EF migration bundle
# is built from the pinned SDK against the same Application/Infrastructure sources as the
# runtime artifacts, then executed as a Container Apps Job before any new API/worker revision
# receives traffic. Application containers never apply schema migrations at startup.
#   docker build -f infra/docker/migrations.Dockerfile -t auditsphere-migrations:<git-sha> .
FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS bundle
ARG DOTNET_ROLL_FORWARD=Major
ENV DOTNET_ROLL_FORWARD=$DOTNET_ROLL_FORWARD
# Match the bounded Release CI compiler configuration. Keep the processor
# limit in the SDK stage so it also applies to EF's child publish process.
ENV DOTNET_PROCESSOR_COUNT=1 DOTNET_gcServer=0 DOTNET_GCConserveMemory=9
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props AuditSphereOps.slnx ./
COPY docs/task_breakdown/tracking/pack_manifest.json docs/task_breakdown/tracking/pack_manifest.json
COPY src/ src/
# The migration bundle only needs executable IL, not analyzer diagnostics or PDBs. The model
# snapshot and migration designers are large generated files; disabling analyzers/PDBs keeps
# the compiler within the image-build runner's memory budget without changing runtime IL.
# EF also generates a RID-specific bundle project and invokes a second `dotnet publish`; pass
# the same compiler, project-reference, and API-host publication limits through the inherited
# MSBuild environment.
RUN dotnet tool install --global dotnet-ef --version 10.0.12 \
  && dotnet restore src/AuditSphereOps.Api/AuditSphereOps.Api.csproj --locked-mode
RUN dotnet build src/AuditSphereOps.Api/AuditSphereOps.Api.csproj \
    --configuration Release --no-restore -m:1 -p:BuildInParallel=false \
    -p:UseSharedCompilation=false -p:PublishAngularUi=false \
    -p:CscEnvironment=DOTNET_PROCESSOR_COUNT=1 \
    -p:RunAnalyzers=false -p:DebugType=None -p:DebugSymbols=false
RUN PATH="$PATH:/root/.dotnet/tools" \
    RunAnalyzers=false BuildInParallel=false UseSharedCompilation=false \
    PublishAngularUi=false DebugType=None DebugSymbols=false \
    CscEnvironment=DOTNET_PROCESSOR_COUNT=1 dotnet ef migrations bundle --configuration Release --no-build \
    --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Api \
    -o /out/efbundle

# Framework-dependent bundle: the self-contained publish of the full design-time host exceeded
# the image job budget. The aspnet runtime supplies Microsoft.AspNetCore.App and the .NET 11
# runtime; the bundle still carries the same migration IL and applies the same SQL.
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
RUN apt-get update \
  && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
  && rm -rf /var/lib/apt/lists/*
COPY --from=bundle --chmod=0555 /out/efbundle /app/efbundle
ENV AUDITSPHERE_MIGRATION_BUNDLE=1
USER $APP_UID
# The design-time factory reads ConnectionStrings__AuditSphere from the job environment.
ENTRYPOINT ["/app/efbundle"]

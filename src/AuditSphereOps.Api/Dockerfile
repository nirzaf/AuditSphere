# Syntax reference: https://docs.docker.com/reference/dockerfile/
# AuditSphere API host: Angular assets + ASP.NET Core API in one immutable runtime image.
# Build context is the repository root so the pinned SDK, central package versions and the
# Angular workspace resolve exactly as the CI build gate does.
#   docker build -f src/AuditSphereOps.Api/Dockerfile -t auditsphere-api:<git-sha> .
FROM mcr.microsoft.com/devcontainers/javascript-node:22-bookworm AS ui
# The devcontainer image defaults to its non-root `node` user; this ephemeral build stage
# writes npm dependencies and Angular output under /ui, so use root only for these build steps.
USER root
WORKDIR /ui
COPY src/AuditSphereOps.Ui/package.json src/AuditSphereOps.Ui/package-lock.json ./
RUN npm ci
COPY src/AuditSphereOps.Ui/ ./
# Production asset set: fingerprinted browser output consumed by the API host.
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS api
ARG DOTNET_ROLL_FORWARD=disable
ENV DOTNET_ROLL_FORWARD=$DOTNET_ROLL_FORWARD
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props AuditSphereOps.slnx ./
COPY src/AuditSphereOps.Domain/AuditSphereOps.Domain.csproj src/AuditSphereOps.Domain/
COPY src/AuditSphereOps.Application/AuditSphereOps.Application.csproj src/AuditSphereOps.Application/
COPY src/AuditSphereOps.Infrastructure/AuditSphereOps.Infrastructure.csproj src/AuditSphereOps.Infrastructure/
COPY src/AuditSphereOps.Api/AuditSphereOps.Api.csproj src/AuditSphereOps.Api/
RUN dotnet restore src/AuditSphereOps.Api/AuditSphereOps.Api.csproj --locked-mode
COPY docs/task_breakdown/tracking/pack_manifest.json docs/task_breakdown/tracking/pack_manifest.json
COPY docs/task_breakdown/tasks/ docs/task_breakdown/tasks/
COPY src/ src/
# Finish the Angular build before the memory-intensive .NET compile starts.
COPY --from=ui /ui/dist/auditsphere-ui/browser/ /ui-assets/
# The Angular asset set is copied from the ui stage; publishing is backend-only here.
RUN dotnet publish src/AuditSphereOps.Api/AuditSphereOps.Api.csproj \
  --configuration Release --no-restore -m:1 -p:BuildInParallel=false \
  -p:UseSharedCompilation=false -p:PublishAngularUi=false -o /out \
  && test -f /out/AuditSphereOps.Api.dll

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api /out/ .
COPY --from=api /ui-assets/ ./ui/
RUN mkdir -p /app/data-protection-home /var/lib/auditsphere/pbc-staging /var/lib/auditsphere/release-checkpoints \
  && chown -R $APP_UID:$APP_UID /app/data-protection-home /var/lib/auditsphere
ENV ASPNETCORE_URLS=http://+:8080 \
  DOTNET_gcServer=1
# The aspnet image ships a non-root user; the host binds 8080 and writes nothing outside mounted volumes.
USER $APP_UID
ENTRYPOINT ["dotnet", "AuditSphereOps.Api.dll"]

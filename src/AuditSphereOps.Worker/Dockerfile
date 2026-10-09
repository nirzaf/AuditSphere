# Syntax reference: https://docs.docker.com/reference/dockerfile/
# AuditSphere worker image. One image serves every worker group (general, processing, records,
# mail, pbc, client-sites): Worker:Group configuration decides the capability composition at
# startup, and privileged Microsoft 365 credentials are only ever mounted to their isolated
# external-effect workers. Build context is the repository root.
#   docker build -f src/AuditSphereOps.Worker/Dockerfile -t auditsphere-worker:<git-sha> .
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
ARG DOTNET_ROLL_FORWARD=disable
ENV DOTNET_ROLL_FORWARD=$DOTNET_ROLL_FORWARD
WORKDIR /repo
COPY global.json Directory.Packages.props AuditSphereOps.slnx ./
COPY src/AuditSphereOps.Domain/AuditSphereOps.Domain.csproj src/AuditSphereOps.Domain/
COPY src/AuditSphereOps.Application/AuditSphereOps.Application.csproj src/AuditSphereOps.Application/
COPY src/AuditSphereOps.Infrastructure/AuditSphereOps.Infrastructure.csproj src/AuditSphereOps.Infrastructure/
COPY src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj src/AuditSphereOps.Worker/
RUN dotnet restore src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/AuditSphereOps.Worker/AuditSphereOps.Worker.csproj \
  --configuration Release --no-restore -o /out \
  && test -f /out/AuditSphereOps.Worker.dll

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ .
RUN mkdir -p /var/lib/auditsphere/pbc-staging /var/lib/auditsphere/release-checkpoints \
  && chown -R $APP_UID:$APP_UID /var/lib/auditsphere
ENV DOTNET_gcServer=1
USER $APP_UID
ENTRYPOINT ["dotnet", "AuditSphereOps.Worker.dll"]

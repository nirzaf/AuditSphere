# Migration bundle image for the dedicated migration deployment step. The EF migration bundle
# is built from the pinned SDK against the same Application/Infrastructure sources as the
# runtime artifacts, then executed as a Container Apps Job before any new API/worker revision
# receives traffic. Application containers never apply schema migrations at startup.
#   docker build -f infra/docker/migrations.Dockerfile -t auditsphere-migrations:<git-sha> .
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS bundle
ARG DOTNET_ROLL_FORWARD=Major
ENV DOTNET_ROLL_FORWARD=$DOTNET_ROLL_FORWARD
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props AuditSphereOps.slnx ./
COPY src/ src/
RUN dotnet tool install --global dotnet-ef --version 10.0.12 \
  && export PATH="$PATH:/root/.dotnet/tools" \
  && dotnet ef migrations bundle --self-contained -r linux-x64 \
    --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Api \
    -o /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app
COPY --from=bundle /out/efbundle /app/efbundle
# The job receives the target connection string as the secret environment variable
# ConnectionStrings__AuditSphere; efbundle applies pending migrations and exits non-zero on failure.
ENTRYPOINT ["/bin/sh", "-c", "/app/efbundle --connection \"$ConnectionStrings__AuditSphere\""]

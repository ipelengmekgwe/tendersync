# syntax=docker/dockerfile:1
# TenderSync .NET images (PLAN.md §15). One build, three targets:
#   api     - TenderSync.Api on :8080
#   worker  - TenderSync.Worker (Hangfire server + dashboard) on :8080
#   migrate - the Worker image running --migrate-seed once (migrations + reference data), then exiting
# Build from the repository root:  docker build -f deploy/docker/dotnet.Dockerfile --target api -t tendersync-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# global.json is left out on purpose: it pins the local SDK feature band, and the image's SDK may be newer or older.
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY src/TenderSync.Domain/TenderSync.Domain.csproj src/TenderSync.Domain/
COPY src/TenderSync.Application/TenderSync.Application.csproj src/TenderSync.Application/
COPY src/TenderSync.Infrastructure/TenderSync.Infrastructure.csproj src/TenderSync.Infrastructure/
COPY src/TenderSync.Contracts/TenderSync.Contracts.csproj src/TenderSync.Contracts/
COPY src/TenderSync.Api/TenderSync.Api.csproj src/TenderSync.Api/
COPY src/TenderSync.Worker/TenderSync.Worker.csproj src/TenderSync.Worker/
RUN dotnet restore src/TenderSync.Api/TenderSync.Api.csproj \
 && dotnet restore src/TenderSync.Worker/TenderSync.Worker.csproj
COPY src/ src/
RUN dotnet publish src/TenderSync.Api/TenderSync.Api.csproj -c Release -o /out/api --no-restore -p:UseAppHost=false \
 && dotnet publish src/TenderSync.Worker/TenderSync.Worker.csproj -c Release -o /out/worker --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DocumentStore__FileSystem__RootPath=/data/documents
# The document store volume mounts here; owned by the image's non-root user so a fresh named volume inherits it.
RUN mkdir -p /data/documents && chown app:app /data/documents
WORKDIR /app
USER app
EXPOSE 8080
# No curl in the aspnet image: bash's /dev/tcp is enough for a liveness probe.
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=6 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health/live HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -n1 <&3 | grep -q " 200 "'

FROM runtime AS api
COPY --from=build --chown=app:app /out/api ./
ENTRYPOINT ["dotnet", "TenderSync.Api.dll"]

FROM runtime AS worker
COPY --from=build --chown=app:app /out/worker ./
ENTRYPOINT ["dotnet", "TenderSync.Worker.dll"]

FROM worker AS migrate
HEALTHCHECK NONE
CMD ["--migrate-seed"]

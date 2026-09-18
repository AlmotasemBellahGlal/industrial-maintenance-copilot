# Shared source/restore cache for the evaluator and production runtime targets.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
WORKDIR /src
COPY . .
RUN --mount=type=cache,id=maintenance-nuget,target=/root/.nuget/packages dotnet restore tools/IndustrialCopilot.Demo/IndustrialCopilot.Demo.csproj
FROM build AS publish-demo
RUN --mount=type=cache,id=maintenance-nuget,target=/root/.nuget/packages dotnet publish tools/IndustrialCopilot.Demo/IndustrialCopilot.Demo.csproj -c Release --no-restore -o /out /p:UseAppHost=false
FROM build AS publish-api
RUN --mount=type=cache,id=maintenance-nuget,target=/root/.nuget/packages dotnet publish src/IndustrialCopilot.Api/IndustrialCopilot.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false
FROM build AS publish-worker
RUN --mount=type=cache,id=maintenance-nuget,target=/root/.nuget/packages dotnet publish src/IndustrialCopilot.Worker/IndustrialCopilot.Worker.csproj -c Release --no-restore -o /out /p:UseAppHost=false
FROM build AS publish-evaluation
RUN --mount=type=cache,id=maintenance-nuget,target=/root/.nuget/packages dotnet publish tools/IndustrialCopilot.Evaluation/IndustrialCopilot.Evaluation.csproj -c Release -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS runtime
WORKDIR /app
USER $APP_UID
FROM runtime AS api
COPY --from=publish-api /out .
ENTRYPOINT ["dotnet", "IndustrialCopilot.Api.dll"]
FROM runtime AS worker
COPY --from=publish-worker /out .
ENTRYPOINT ["dotnet", "IndustrialCopilot.Worker.dll"]
FROM runtime AS demo
COPY --from=publish-demo /out .
COPY demo /app/demo
ENTRYPOINT ["dotnet", "IndustrialCopilot.Demo.dll", "--container"]
FROM runtime AS evaluation
COPY --from=publish-evaluation /out .
COPY evaluation /app/evaluation
COPY --chmod=755 deploy/evaluate.sh /app/evaluate.sh
USER root
RUN mkdir -p /app/artifacts/evaluation && chown -R $APP_UID:$APP_UID /app/artifacts
USER $APP_UID
ENTRYPOINT ["/app/evaluate.sh"]

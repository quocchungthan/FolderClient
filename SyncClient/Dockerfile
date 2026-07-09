FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG GH_NUGET_TOKEN=
COPY . .
RUN if [ -n "$GH_NUGET_TOKEN" ]; then \
			dotnet nuget add source "https://nuget.pkg.github.com/quocchungthan/index.json" \
				--name github --username "quocchungthan" --password "$GH_NUGET_TOKEN" --store-password-in-clear-text; \
		fi && \
		dotnet restore FolderClient/SyncClient/SyncClient.csproj
RUN dotnet publish FolderClient/SyncClient/SyncClient.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SyncClient.dll"]

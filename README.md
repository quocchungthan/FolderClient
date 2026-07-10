```
cd ~/src/FolderClient

dotnet restore FolderClient.slnx
dotnet build FolderClient.slnx -c Release
dotnet publish SyncClient/SyncClient.csproj -c Release -o ./artifacts/publish


mkdir -p /c/sync-data

cd ./artifacts/publish && ASPNETCORE_ENVIRONMENT=Production ./SyncClient.exe
```

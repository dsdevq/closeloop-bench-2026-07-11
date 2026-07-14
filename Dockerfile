# Stage 1: Node — build Angular production bundle
FROM node:22-alpine AS node-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npx ng build --configuration production

# Stage 2: .NET SDK 9.0 — restore, build, publish API
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dotnet-build
WORKDIR /src
COPY global.json ./
COPY backend/ ./backend/
RUN dotnet restore backend/Api/Api.csproj
RUN dotnet publish backend/Api/Api.csproj \
      --configuration Release \
      --no-restore \
      --output /publish

# Stage 3: ASP.NET Core 9.0 runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=dotnet-build /publish ./
# Angular browser bundle copied to wwwroot/ — served by UseStaticFiles() once wired in Program.cs
COPY --from=node-build /src/frontend/dist/frontend/browser ./wwwroot/
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]

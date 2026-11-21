FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY data ./data
COPY src/MerciTantine.Core ./src/MerciTantine.Core
COPY src/MerciTantine.Server ./src/MerciTantine.Server
RUN dotnet publish src/MerciTantine.Server -c Release -o /app --no-self-contained
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV SAVE_DIRECTORY=/var/data/merci-tantine
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "exec dotnet MerciTantine.Server.dll --urls http://0.0.0.0:${PORT:-10000}"]

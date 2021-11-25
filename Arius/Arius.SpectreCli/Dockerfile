# Local build:  
# docker build . -t arius2 --build-arg GITHUB_USER=wouter.vanranst@gmail.com --build-arg GITHUB_TOKEN=....

# https://hub.docker.com/_/microsoft-dotnet-runtime/
FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
# RUN apt-get update
WORKDIR /app


# https://hub.docker.com/_/microsoft-dotnet-sdk/
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG GITHUB_USER
ARG GITHUB_TOKEN
WORKDIR /src
COPY "Arius.CliSpectre.csproj" .
RUN dotnet nuget add source -u $GITHUB_USER -p $GITHUB_TOKEN --store-password-in-clear-text -n AriusGithub "https://nuget.pkg.github.com/woutervanranst/index.json"
RUN dotnet restore "Arius.CliSpectre.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "Arius.CliSpectre.csproj" -c Release -o /app/build


FROM build AS publish
RUN dotnet publish "Arius.CliSpectre.csproj" -c Release -o /app/publish


FROM base as final
VOLUME ["/archive"]
VOLUME ["/logs"]
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "arius.dll"]
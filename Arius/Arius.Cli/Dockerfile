# Local build:  docker build . -t arius

FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
RUN apt-get update
WORKDIR /app


FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
ARG GITHUB_USER
ARG GITHUB_TOKEN
WORKDIR /src
COPY "Arius.Cli.csproj" .
RUN dotnet nuget add source -u $GITHUB_USER -p $GITHUB_TOKEN --store-password-in-clear-text -n AriusGithub "https://nuget.pkg.github.com/woutervanranst/index.json"
RUN dotnet restore "Arius.Cli.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "Arius.Cli.csproj" -c Release -o /app/build


FROM build AS publish
RUN dotnet publish "Arius.Cli.csproj" -c Release -o /app/publish


FROM base as azcopy
RUN apt-get install -y \
    wget \
    tar
RUN wget https://aka.ms/downloadazcopy-v10-linux
RUN tar -xvf downloadazcopy-v10-linux --no-anchored azcopy --strip-components 1
    # Extract azcopy binary to /app/azcopy
    #   xvf: extract verbose file
    #   --no-anchored azcopy: The binary resides in a subfolder of the archive. This flag extract all files matching 'azcopy' 
    #   --strip-components 1: flatten / do not extract to same directory structure


FROM base as final
VOLUME ["/archive"]
VOLUME ["/logs"]
RUN apt-get install p7zip-full -y
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=azcopy /app/azcopy /usr/bin/
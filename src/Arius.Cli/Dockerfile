#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Local build
# in /src/
# docker build -f Arius.Cli\Dockerfile . -t arius

# Local run (in cmd.exe, not WSL)
# docker run -it arius /bin/sh

# Base rintime image for final image
# https://hub.docker.com/_/microsoft-dotnet-runtime/
FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

# SDK image for build & test
# https://hub.docker.com/_/microsoft-dotnet-sdk/
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build

# Copy project files
WORKDIR /src
COPY ["Arius.Cli/Arius.Cli.csproj", "Arius.Cli/"]
COPY ["Arius.Cli.Tests/Arius.Cli.Tests.csproj", "Arius.Cli.Tests/"]

# Restore the projects
RUN dotnet restore "Arius.Cli/Arius.Cli.csproj"
RUN dotnet restore "Arius.Cli.Tests/Arius.Cli.Tests.csproj"

# Copy the source code
COPY . .

# Build the CLI project
RUN dotnet build "Arius.Cli/Arius.Cli.csproj" -c Release -o /app/build

# Run the CLI test project _in_ the container
RUN mkdir /logs
RUN dotnet test "Arius.Cli.Tests/Arius.Cli.Tests.csproj"

# If the test was successful, create final artifact
FROM build AS publish
RUN dotnet publish "Arius.Cli/Arius.Cli.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
VOLUME ["/archive"]
VOLUME ["/logs"]
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "arius.dll"]

#ENTRYPOINT ["/bin/sh", "-c"]
#CMD ["while :; do sleep 10; done"]
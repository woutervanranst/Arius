# Local build
# in /src/
# docker build -f Arius.Cli\Dockerfile . -t arius

# Local run (in cmd.exe, not WSL)
# docker run -it arius /bin/sh


# Use the official .NET SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the solution and project files
COPY ["Arius.Cli/Arius.Cli.csproj", "Arius.Cli/"]
COPY ["Arius.Core/Arius.Core.csproj", "Arius.Core/"]

# Restore dependencies
RUN dotnet restore "Arius.Cli/Arius.Cli.csproj"

# Copy the remaining source code and build the application
COPY . .
WORKDIR "Arius.Cli"
RUN dotnet build "Arius.Cli.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "Arius.Cli.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official .NET runtime image for the final application
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

# Copy the published application
COPY --from=publish /app/publish .

# Define mount points for archive and logs
VOLUME /archive
VOLUME /logs

# Set the entry point for the application
ENTRYPOINT ["dotnet", "arius.dll"]
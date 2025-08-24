FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

# Copy the prebuilt artifacts into the image
COPY publish/ .

# Define mount points for archive and logs
VOLUME /archive
VOLUME /logs

ENTRYPOINT ["dotnet", "arius.dll"]

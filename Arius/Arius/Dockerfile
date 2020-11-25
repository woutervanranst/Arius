FROM mcr.microsoft.com/dotnet/runtime:5.0-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
#WORKDIR /
COPY ["Arius.csproj", "Arius/"]
RUN dotnet restore "Arius/Arius.csproj"
COPY . /Arius
WORKDIR "/Arius"
RUN dotnet build "Arius.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Arius.csproj" -c Release -o /app/publish

FROM base AS final
RUN apt-get update && apt-get install -y \
    wget \
    tar
RUN wget https://aka.ms/downloadazcopy-v10-linux
RUN tar -xvf downloadazcopy-v10-linux
RUN cp ./azcopy_linux_amd64_*/azcopy /usr/bin/
#WORKDIR /app
COPY --from=publish /app/publish .
# NOTE: Debugging in VS overwrites this
#ENTRYPOINT ["dotnet", "arius.dll", "ha"]
#CMD ["dotnet arius.dll archive "]

##See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
#
#FROM mcr.microsoft.com/dotnet/runtime:5.0-buster-slim AS base
#WORKDIR /app
#
#FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
#WORKDIR /src
#COPY ["Arius/Arius.csproj", "Arius/"]
#RUN dotnet restore "Arius/Arius.csproj"
#COPY . .
#WORKDIR "/src/Arius"
#RUN dotnet build "Arius.csproj" -c Release -o /app/build
#
#FROM build AS publish
#RUN dotnet publish "Arius.csproj" -c Release -o /app/publish
#
#FROM base AS final
#WORKDIR /app
#COPY --from=publish /app/publish .
#ENTRYPOINT ["dotnet", "arius.dll"]
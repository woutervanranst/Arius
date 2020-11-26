#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.


# BUILD WITH            docker build -f Dockerfile .. -t arius


FROM mcr.microsoft.com/dotnet/runtime:5.0-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /src
COPY ["Arius/Arius.csproj", "Arius/"]
RUN dotnet restore "Arius/Arius.csproj"
COPY . .
WORKDIR "/src/Arius"
RUN dotnet build "Arius.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Arius.csproj" -c Release -o /app/publish

FROM build AS tooling
RUN apt-get update && apt-get install -y \
    wget \
    tar
RUN wget https://aka.ms/downloadazcopy-v10-linux
RUN tar -xvf downloadazcopy-v10-linux
RUN cp ./azcopy_linux_amd64_*/azcopy /usr/bin/

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=tooling /usr/bin/azcopy /usr/bin/
## NOTE: Debugging in VS overwrites this
#ENTRYPOINT ["dotnet", "arius.dll"]
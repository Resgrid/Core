#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
ARG BUILD_VERSION=3.5.0

#FROM mcr.microsoft.com/dotnet/runtime:9.0.3-noble-amd64 AS base
FROM dhi.io/dotnet:9.0.16-debian13 AS base
ARG BUILD_VERSION
WORKDIR /app
EXPOSE 80

#FROM mcr.microsoft.com/dotnet/sdk:9.0.202-noble-amd64 AS build
FROM dhi.io/dotnet:9.0.314-sdk-debian13 AS build
ARG BUILD_VERSION
WORKDIR /src
COPY ["Web/Resgrid.Web.Eventing/Resgrid.Web.Eventing.csproj", "Web/Resgrid.Web.Eventing/"]
COPY ["Providers/Resgrid.Providers.Bus.Rabbit/Resgrid.Providers.Bus.Rabbit.csproj", "Providers/Resgrid.Providers.Bus.Rabbit/"]
COPY ["Core/Resgrid.Framework/Resgrid.Framework.csproj", "Core/Resgrid.Framework/"]
COPY ["Core/Resgrid.Config/Resgrid.Config.csproj", "Core/Resgrid.Config/"]
COPY ["Core/Resgrid.Model/Resgrid.Model.csproj", "Core/Resgrid.Model/"]
COPY ["Core/Resgrid.Services/Resgrid.Services.csproj", "Core/Resgrid.Services/"]
COPY ["Providers/Resgrid.Providers.Bus/Resgrid.Providers.Bus.csproj", "Providers/Resgrid.Providers.Bus/"]
COPY ["Providers/Resgrid.Providers.Cache/Resgrid.Providers.Cache.csproj", "Providers/Resgrid.Providers.Cache/"]
RUN dotnet restore "Web/Resgrid.Web.Eventing/Resgrid.Web.Eventing.csproj"
COPY . .
WORKDIR "/src/Web/Resgrid.Web.Eventing"

FROM build AS publish
ARG BUILD_VERSION
RUN dotnet publish "Resgrid.Web.Eventing.csproj" -c Release -o /app/publish  -p:Version=${BUILD_VERSION}
ADD https://github.com/ufoscout/docker-compose-wait/releases/download/2.9.0/wait /app/publish/wait
RUN chmod +x /app/publish/wait

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./wait"]
CMD ["dotnet", "Resgrid.Web.Eventing.dll"]

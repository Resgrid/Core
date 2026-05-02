ARG BUILD_VERSION=3.5.0

FROM mcr.microsoft.com/dotnet/aspnet:9.0.3-noble-amd64 AS base
ARG BUILD_VERSION
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0.202-noble-amd64 AS build
ARG BUILD_VERSION
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["Web/Resgrid.Web.Tts/Resgrid.Web.Tts.csproj", "Web/Resgrid.Web.Tts/"]
COPY ["Core/Resgrid.Config/Resgrid.Config.csproj", "Core/Resgrid.Config/"]
RUN dotnet restore "Web/Resgrid.Web.Tts/Resgrid.Web.Tts.csproj"

COPY . .
WORKDIR /src/Web/Resgrid.Web.Tts
RUN dotnet publish "Resgrid.Web.Tts.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore -p:Version=${BUILD_VERSION}

FROM base AS final
RUN apt-get update \
	&& apt-get install -y --no-install-recommends espeak-ng ffmpeg ca-certificates \
	&& rm -rf /var/lib/apt/lists/* \
	&& groupadd --gid 10001 appgroup \
	&& useradd --uid 10001 --gid appgroup --create-home --shell /usr/sbin/nologin appuser

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
USER appuser
ENTRYPOINT ["dotnet", "Resgrid.Web.Tts.dll"]

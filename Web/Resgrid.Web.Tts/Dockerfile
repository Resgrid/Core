ARG BUILD_VERSION=3.5.0

#FROM mcr.microsoft.com/dotnet/runtime:9.0.3-noble-amd64 AS base
FROM dhi.io/dotnet:9.0.16-debian13@sha256:4797903e46b475875f4a402aa6f42a1dcc78cfe67d217f9f5f49e358b60d47a3 AS base
ARG BUILD_VERSION
WORKDIR /app
EXPOSE 8080

#FROM mcr.microsoft.com/dotnet/sdk:9.0.202-noble-amd64 AS build
FROM dhi.io/dotnet:9.0.314-sdk-debian13@sha256:a3acd51de0af79878e26292b3053aab513c6ca4476ddb2f9f11adb9c04aa7c89 AS build
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
# Install ffmpeg, download Piper TTS binary and the default English (US) voice
# model, then clean up in a single RUN layer to keep the image small.
ARG PIPER_VERSION=v1.2.0
RUN apt-get update \
	&& apt-get install -y --no-install-recommends \
		ffmpeg \
		libespeak-ng1 \
		ca-certificates \
		curl \
		libc-bin \
		passwd \
	&& rm -rf /var/lib/apt/lists/* \
	&& mkdir -p /usr/local/share/piper-voices \
	&& curl -fsSL --retry 3 --retry-delay 5 "https://github.com/rhasspy/piper/releases/download/${PIPER_VERSION}/piper_amd64.tar.gz" -o /tmp/piper.tar.gz \
	&& tar -xzf /tmp/piper.tar.gz -C /tmp \
	&& mv /tmp/piper/piper /usr/local/bin/piper \
	&& chmod +x /usr/local/bin/piper \
	&& find /tmp/piper -name '*.so*' -exec cp {} /usr/local/lib/ \; \
	&& if [ -d /tmp/piper/espeak-ng-data ]; then cp -R /tmp/piper/espeak-ng-data /usr/share/; else ln -sf /usr/lib/x86_64-linux-gnu/espeak-ng-data /usr/share/espeak-ng-data; fi \
	&& ldconfig \
	&& rm -rf /tmp/piper /tmp/piper.tar.gz \
	&& for f in \
		"en/en_US/norman/medium/en_US-norman-medium" \
		"es/es_MX/claude/high/es_MX-claude-high" \
		"sv/sv_SE/nst/medium/sv_SE-nst-medium" \
		"de/de_DE/thorsten/medium/de_DE-thorsten-medium" \
		"fr/fr_FR/siwis/medium/fr_FR-siwis-medium" \
		"it/it_IT/paola/medium/it_IT-paola-medium" \
		"pl/pl_PL/gosia/medium/pl_PL-gosia-medium" \
		"uk/uk_UA/ukrainian_tts/medium/uk_UA-ukrainian_tts-medium" \
		"ar/ar_JO/kareem/medium/ar_JO-kareem-medium" \
	; do \
		name=$(basename "$f"); \
		curl -fsSL --retry 3 --retry-delay 5 "https://huggingface.co/rhasspy/piper-voices/resolve/main/${f}.onnx" -o "/usr/local/share/piper-voices/${name}.onnx"; \
		curl -fsSL --retry 3 --retry-delay 5 "https://huggingface.co/rhasspy/piper-voices/resolve/main/${f}.onnx.json" -o "/usr/local/share/piper-voices/${name}.onnx.json"; \
	done \
	&& groupadd --gid 10001 appgroup \
	&& useradd --uid 10001 --gid appgroup --create-home --shell /usr/sbin/nologin appuser

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
USER appuser
ENTRYPOINT ["dotnet", "Resgrid.Web.Tts.dll"]

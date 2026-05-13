ARG BUILD_VERSION=3.5.0

#FROM mcr.microsoft.com/dotnet/runtime:9.0.3-noble-amd64 AS base
FROM dhi.io/dotnet:9.0.16-debian13 AS base
ARG BUILD_VERSION
WORKDIR /app
EXPOSE 8080

#FROM mcr.microsoft.com/dotnet/sdk:9.0.202-noble-amd64 AS build
FROM dhi.io/dotnet:9.0.314-sdk-debian13 AS build
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
	&& mkdir -p /usr/local/share/piper-voices \
	&& curl -fsSL "https://github.com/rhasspy/piper/releases/download/${PIPER_VERSION}/piper_amd64.tar.gz" -o /tmp/piper.tar.gz \
	&& tar -xzf /tmp/piper.tar.gz -C /tmp \
	&& mv /tmp/piper/piper /usr/local/bin/piper \
	&& chmod +x /usr/local/bin/piper \
	&& find /tmp/piper -name '*.so*' -exec cp {} /usr/local/lib/ \; \
	&& if [ -d /tmp/piper/espeak-ng-data ]; then cp -R /tmp/piper/espeak-ng-data /usr/share/; else ln -sf /usr/lib/x86_64-linux-gnu/espeak-ng-data /usr/share/espeak-ng-data; fi \
	&& ldconfig \
	&& rm -rf /tmp/piper /tmp/piper.tar.gz \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/norman/medium/en_US-norman-medium.onnx" -o /usr/local/share/piper-voices/en_US-norman-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/norman/medium/en_US-norman-medium.onnx.json" -o /usr/local/share/piper-voices/en_US-norman-medium.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/es/es_MX/claude/high/es_MX-claude-high.onnx" -o /usr/local/share/piper-voices/es_MX-claude-high.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/es/es_MX/claude/high/es_MX-claude-high.onnx.json" -o /usr/local/share/piper-voices/es_MX-claude-high.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/sv/sv_SE/nst/medium/sv_SE-nst-medium.onnx" -o /usr/local/share/piper-voices/sv_SE-nst-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/sv/sv_SE/nst/medium/sv_SE-nst-medium.onnx.json" -o /usr/local/share/piper-voices/sv_SE-nst-medium.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/de/de_DE/thorsten/medium/de_DE-thorsten-medium.onnx" -o /usr/local/share/piper-voices/de_DE-thorsten-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/de/de_DE/thorsten/medium/de_DE-thorsten-medium.onnx.json" -o /usr/local/share/piper-voices/de_DE-thorsten-medium.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/siwis/medium/fr_FR-siwis-medium.onnx" -o /usr/local/share/piper-voices/fr_FR-siwis-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/siwis/medium/fr_FR-siwis-medium.onnx.json" -o /usr/local/share/piper-voices/fr_FR-siwis-medium.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/it/it_IT/paola/medium/it_IT-paola-medium.onnx" -o /usr/local/share/piper-voices/it_IT-paola-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/it/it_IT/paola/medium/it_IT-paola-medium.onnx.json" -o /usr/local/share/piper-voices/it_IT-paola-medium.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/pl/pl_PL/gosia/medium/pl_PL-gosia-medium.onnx" -o /usr/local/share/piper-voices/pl_PL-gosia-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/pl/pl_PL/gosia/medium/pl_PL-gosia-medium.onnx.json" -o /usr/local/share/piper-voices/pl_PL-gosia-medium.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/uk/uk_UA/ukrainian_tts/medium/uk_UA-ukrainian_tts-medium.onnx" -o /usr/local/share/piper-voices/uk_UA-ukrainian_tts-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/uk/uk_UA/ukrainian_tts/medium/uk_UA-ukrainian_tts-medium.onnx.json" -o /usr/local/share/piper-voices/uk_UA-ukrainian_tts-medium.onnx.json \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/ar/ar_JO/kareem/medium/ar_JO-kareem-medium.onnx" -o /usr/local/share/piper-voices/ar_JO-kareem-medium.onnx \
	&& curl -fsSL "https://huggingface.co/rhasspy/piper-voices/resolve/main/ar/ar_JO/kareem/medium/ar_JO-kareem-medium.onnx.json" -o /usr/local/share/piper-voices/ar_JO-kareem-medium.onnx.json \
	&& rm -rf /var/lib/apt/lists/* \
	&& groupadd --gid 10001 appgroup \
	&& useradd --uid 10001 --gid appgroup --create-home --shell /usr/sbin/nologin appuser

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
USER appuser
ENTRYPOINT ["dotnet", "Resgrid.Web.Tts.dll"]

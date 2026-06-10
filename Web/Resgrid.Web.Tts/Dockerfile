ARG BUILD_VERSION=3.5.0

#FROM mcr.microsoft.com/dotnet/runtime:9.0.3-noble-amd64 AS base
FROM dhi.io/aspnetcore:9.0.16-debian13@sha256:961647e80202ce33fc06472dda4e7ae2d2bc56d819aee6742602f70047b13dc7 AS base
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

FROM build AS publish
ARG PIPER_VERSION=v1.2.0
COPY --from=base /etc/passwd /tmp/base-passwd
COPY --from=base /etc/group /tmp/base-group
RUN apt-get update \
	&& apt-get install -y --no-install-recommends \
		libespeak-ng1 \
		ca-certificates \
		curl \
		xz-utils \
		gzip \
		passwd \
		tzdata \
	&& rm -rf /var/lib/apt/lists/* \
	&& curl -fsSL --retry 3 --retry-delay 5 "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz" -o /tmp/ffmpeg.tar.xz \
	&& curl -fsSL "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz.md5" -o /tmp/ffmpeg.tar.xz.md5 \
	&& echo "$(awk '{print $1}' /tmp/ffmpeg.tar.xz.md5)  /tmp/ffmpeg.tar.xz" | md5sum -c - \
	&& rm -f /tmp/ffmpeg.tar.xz.md5 \
	&& xz -d /tmp/ffmpeg.tar.xz \
	&& tar -xf /tmp/ffmpeg.tar -C /tmp \
	&& mkdir -p /usr/local/bin \
	&& ffmpeg_path=$(find /tmp -name 'ffmpeg' -type f -executable -not -path '*/lib/*' -print -quit) \
	&& test -n "$ffmpeg_path" && cp "$ffmpeg_path" /usr/local/bin/ffmpeg \
	&& ffprobe_path=$(find /tmp -name 'ffprobe' -type f -executable -not -path '*/lib/*' -print -quit) \
	&& test -n "$ffprobe_path" && cp "$ffprobe_path" /usr/local/bin/ffprobe \
	&& chmod +x /usr/local/bin/ffmpeg /usr/local/bin/ffprobe \
	&& rm -rf /tmp/ffmpeg* \
	&& mkdir -p /usr/local/share/piper-voices \
	&& curl -fsSL --retry 3 --retry-delay 5 "https://github.com/rhasspy/piper/releases/download/${PIPER_VERSION}/piper_amd64.tar.gz" -o /tmp/piper.tar.gz \
	&& tar -xzf /tmp/piper.tar.gz -C /tmp \
	&& mv /tmp/piper/piper /usr/local/bin/piper \
	&& chmod +x /usr/local/bin/piper \
	&& mkdir -p /usr/local/lib \
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
	&& useradd --uid 10001 --gid appgroup --create-home --shell /usr/sbin/nologin appuser \
	&& cp /tmp/base-passwd /tmp/app-passwd \
	&& cp /tmp/base-group /tmp/app-group \
	&& grep appuser /etc/passwd >> /tmp/app-passwd \
	&& grep appgroup /etc/group >> /tmp/app-group \
	&& mkdir -p /tmp/ttsdeps \
	&& for bin in /usr/local/bin/piper /usr/local/bin/ffmpeg; do \
		ldd "$bin" 2>/dev/null | awk '/=>/ {print $3}' >> /tmp/ttsdeps/libs.txt; \
	done \
	&& find /usr/local/lib -name '*.so*' -type f >> /tmp/ttsdeps/libs.txt \
	&& sort -u /tmp/ttsdeps/libs.txt | while read lib; do [ -f "$lib" ] && cp "$lib" /tmp/ttsdeps/; done

FROM base AS final
WORKDIR /app

COPY --from=publish /usr/local/bin/piper /usr/local/bin/piper
COPY --from=publish /usr/local/share/piper-voices/ /usr/local/share/piper-voices/
COPY --from=publish /usr/share/espeak-ng-data/ /usr/share/espeak-ng-data/
COPY --from=publish /usr/local/bin/ffmpeg /usr/local/bin/ffmpeg
COPY --from=publish /usr/local/bin/ffprobe /usr/local/bin/ffprobe
COPY --from=publish /tmp/ttsdeps/ /usr/local/lib/tts/
COPY --from=publish /tmp/app-passwd /etc/passwd
COPY --from=publish /tmp/app-group /etc/group
## Copy the IANA timezone database. DHI/distroless bases ship without tzdata,
## so TimeZoneInfo (and TZConvert) can't resolve any zone -> TimeZoneNotFoundException.
COPY --from=publish /usr/share/zoneinfo /usr/share/zoneinfo
ENV TZ=Etc/UTC
COPY --from=publish /app/publish .

ENV LD_LIBRARY_PATH="/usr/local/lib/tts"
ENV ASPNETCORE_URLS=http://+:8080
USER appuser
ENTRYPOINT ["dotnet", "Resgrid.Web.Tts.dll"]

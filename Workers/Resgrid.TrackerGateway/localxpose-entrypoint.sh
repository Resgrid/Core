#!/usr/bin/dash
set -u

LOCLX_BIN="${LOCLX_BIN:-/app/loclx}"
CONFIG_FILE="${LOCALXPOSE_CONFIG_FILE:-/tmp/localxpose-tunnels.yaml}"
REGION="${LOCALXPOSE_REGION:-us}"
TARGET_HOST="${LOCALXPOSE_TARGET_HOST:-localhost}"
RESTART_DELAY="${LOCALXPOSE_RESTART_DELAY:-5}"

lookup_reserved() {
	_pairs="${LOCALXPOSE_RESERVED_ENDPOINTS:-}"
	_oldifs=$IFS
	IFS=','
	for _pair in $_pairs; do
		case "$_pair" in
			"$1="*)
				printf '%s' "${_pair#*=}"
				IFS=$_oldifs
				return 0
				;;
		esac
	done
	IFS=$_oldifs
	return 0
}

enabled=$(printf '%s' "${LOCALXPOSE_ENABLED:-false}" | tr '[:upper:]' '[:lower:]')

if [ "$enabled" = "true" ] || [ "$enabled" = "1" ] || [ "$enabled" = "yes" ]; then
	if [ -z "${LOCALXPOSE_ACCESS_TOKEN:-}" ]; then
		echo "[localxpose] enabled but LOCALXPOSE_ACCESS_TOKEN is empty; tunnels disabled." >&2
	elif [ ! -x "$LOCLX_BIN" ]; then
		echo "[localxpose] $LOCLX_BIN is missing or not executable; tunnels disabled." >&2
	else
		export ACCESS_TOKEN="$LOCALXPOSE_ACCESS_TOKEN"
		export LX_ACCESS_TOKEN="$LOCALXPOSE_ACCESS_TOKEN"

		: > "$CONFIG_FILE"
		tcp_count=0
		for port in ${LOCALXPOSE_TCP_PORTS:-5004 5023 5027}; do
			reserved=$(lookup_reserved "$port")
			{
				printf 'tcp-%s:\n' "$port"
				printf '  type: tcp\n'
				printf '  region: %s\n' "$REGION"
				printf '  to: %s:%s\n' "$TARGET_HOST" "$port"
				if [ -n "$reserved" ]; then
					printf '  reserved_endpoint: %s\n' "$reserved"
				fi
				printf '\n'
			} >> "$CONFIG_FILE"
			tcp_count=$((tcp_count + 1))
		done

		if [ "$tcp_count" -gt 0 ]; then
			echo "[localxpose] starting $tcp_count TCP tunnel(s) from $CONFIG_FILE (region: $REGION)"
			(
				while true; do
					"$LOCLX_BIN" tunnel -r config -f "$CONFIG_FILE"
					echo "[localxpose] TCP tunnel process exited; restarting in ${RESTART_DELAY}s." >&2
					sleep "$RESTART_DELAY"
				done
			) &
		fi

		for port in ${LOCALXPOSE_UDP_PORTS:-}; do
			reserved=$(lookup_reserved "$port")
			echo "[localxpose] starting UDP tunnel for ${TARGET_HOST}:${port} (region: $REGION)"
			(
				while true; do
					if [ -n "$reserved" ]; then
						"$LOCLX_BIN" tunnel -r udp --port "$port" --to "${TARGET_HOST}:${port}" --reserved-endpoint "$reserved"
					else
						"$LOCLX_BIN" tunnel -r udp --port "$port" --to "${TARGET_HOST}:${port}"
					fi
					echo "[localxpose] UDP tunnel for port ${port} exited; restarting in ${RESTART_DELAY}s." >&2
					sleep "$RESTART_DELAY"
				done
			) &
		done
	fi
fi

exec ./wait

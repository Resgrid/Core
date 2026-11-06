"""Forwards localhost ports inside a container's network namespace to the gateway (TCP, TLS untouched).

Core's hosts call their own public URLs (eventing fetches the API's OpenID configuration from ResgridApiBaseUrl,
https://localhost:5443). Inside a container localhost is the container itself, so a sidecar sharing its network
namespace (compose network_mode: service:<host>) runs:  python loopback.py 5443=gateway:8443 5444=gateway:8444
"""
import asyncio
import sys


async def pipe(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
    try:
        while data := await reader.read(65536):
            writer.write(data)
            await writer.drain()
    except (ConnectionError, asyncio.CancelledError):
        pass
    finally:
        writer.close()


def handler(host: str, port: int):
    async def handle(client_reader: asyncio.StreamReader, client_writer: asyncio.StreamWriter) -> None:
        try:
            upstream_reader, upstream_writer = await asyncio.open_connection(host, port)
        except OSError:
            client_writer.close()
            return
        await asyncio.gather(pipe(client_reader, upstream_writer), pipe(upstream_reader, client_writer))

    return handle


async def main(mappings: list[str]) -> None:
    servers = []
    for mapping in mappings:
        listen, target = mapping.split('=')
        host, port = target.rsplit(':', 1)
        servers.append(await asyncio.start_server(handler(host, int(port)), '127.0.0.1', int(listen)))
        print(f'localhost:{listen} -> {target}', flush=True)
    await asyncio.gather(*(server.serve_forever() for server in servers))


if __name__ == '__main__':
    asyncio.run(main(sys.argv[1:]))

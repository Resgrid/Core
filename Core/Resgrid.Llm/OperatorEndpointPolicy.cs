using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Resgrid.Llm;

/// <summary>Private endpoints require an operator-owned exact URI; department overrides retain public HTTPS only.</summary>
public static class OperatorEndpointPolicy
{
    public const string ChatCompletionsPath = "/v1/chat/completions";
    /// <summary>The decision-model route (SGLang serving Clef / Clef-Flash).</summary>
    public const string DecisionPath = "/v1/systemone";

    public static Uri ValidateUri(string endpoint, bool operatorPrivateEndpoint) => ValidateUri(endpoint, operatorPrivateEndpoint, ChatCompletionsPath);

    public static Uri ValidateUri(string endpoint, bool operatorPrivateEndpoint, string requiredPath)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0 ||
            requiredPath is not (ChatCompletionsPath or DecisionPath) || uri.AbsolutePath != requiredPath || !(uri.Scheme == "https" || operatorPrivateEndpoint && uri.Scheme == "http"))
            throw new LlmUnavailableException();
        return uri;
    }

    private static readonly Regex WorkersAiDecisionPath = new("\\A/client/v4/accounts/[0-9a-f]{32}/ai/run/@cf/cloudflare/(clef|clef-flash)\\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// The Cloudflare Workers AI REST route for a Clef model, called directly on api.cloudflare.com over HTTPS (never through AI
    /// Gateway, whose request logging would keep department messages). <paramref name="model"/> is the Workers AI selector.
    /// </summary>
    public static Uri ValidateWorkersAiDecisionUri(string endpoint, out string model)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Query.Length != 0 || !string.Equals(uri.Host, "api.cloudflare.com", StringComparison.Ordinal))
            throw new LlmUnavailableException();
        var match = WorkersAiDecisionPath.Match(uri.AbsolutePath);
        if (!match.Success) throw new LlmUnavailableException();
        model = match.Groups[1].Value;
        return uri;
    }

    public static bool IsAllowedAddress(IPAddress address, bool operatorPrivateEndpoint, bool clearText)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        bool local;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // Always deny metadata/link-local, unspecified, multicast, reserved and broadcast ranges.
            if (bytes[0] == 0 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2 || bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100) || bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return false;
            local = bytes[0] is 10 or 127 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return false;
            local = address.Equals(IPAddress.IPv6Loopback) || (bytes[0] & 0xFE) == 0xFC;
            // Public IPv6 must be global unicast, excluding transition/translation ranges.
            if (!local && ((bytes[0] & 0xE0) != 0x20 || bytes[0] == 0x20 && bytes[1] == 0x02 || bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] == 0 && bytes[3] == 0 || bytes[2] == 0x0d && bytes[3] == 0xb8))) return false;
        }
        else return false;
        return local ? operatorPrivateEndpoint : !clearText;
    }

    private const int SharedClientLimit = 256;
    private static readonly ConcurrentDictionary<(string Scheme, string Host, int Port, bool Private), HttpClient> SharedClients = new();

    /// <summary>
    /// A pooled client per destination for callers that send many requests, so each one does not pay a new TCP and TLS
    /// handshake. The handler pins scheme, host, port and private-access mode, and PooledConnectionLifetime re-runs the
    /// address check on new connections. Callers must not dispose the result.
    /// </summary>
    public static HttpClient GetSharedClient(Uri endpoint, bool operatorPrivateEndpoint)
    {
        EnsureAllowedEndpoint(endpoint, operatorPrivateEndpoint);
        var key = (endpoint.Scheme, endpoint.IdnHost.ToLowerInvariant(), endpoint.Port, operatorPrivateEndpoint);
        if (SharedClients.TryGetValue(key, out var client)) return client;
        // Department endpoints add keys, so a full cache starts over. Dropped clients are not disposed because a caller may
        // still be using one; their idle connections close on their own.
        if (SharedClients.Count >= SharedClientLimit) SharedClients.Clear();
        return SharedClients.GetOrAdd(key, _ => CreateClient(endpoint, operatorPrivateEndpoint));
    }

    private static void EnsureAllowedEndpoint(Uri endpoint, bool operatorPrivateEndpoint)
    {
        if (endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 || !(endpoint.Scheme == "https" || operatorPrivateEndpoint && endpoint.Scheme == "http")) throw new LlmUnavailableException();
    }

    public static HttpClient CreateClient(Uri endpoint, bool operatorPrivateEndpoint)
    {
        EnsureAllowedEndpoint(endpoint, operatorPrivateEndpoint);
        var handler = new SocketsHttpHandler {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, ct) => {
                if (!string.Equals(context.DnsEndPoint.Host, endpoint.IdnHost, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != endpoint.Port) throw new LlmUnavailableException();
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                if (addresses.Length == 0 || addresses.Any(ip => !IsAllowedAddress(ip, operatorPrivateEndpoint, endpoint.Scheme == "http"))) throw new LlmUnavailableException();
                // Connect to the validated literal; no second DNS resolution or redirect can rebind the destination.
                var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(addresses[0], endpoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
}

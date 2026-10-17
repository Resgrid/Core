using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Ai;

/// <summary>
/// Prefix-cache partition per department on the shared operator endpoint (vLLM <c>cache_salt</c>). Requests reuse cached prompt
/// blocks only with the same department's requests, so prefill latency cannot reveal whether another department sent a given
/// prompt. The salt is an HMAC of the department id, so it is stable (reuse within the department still works) and reveals
/// nothing. Callers pass the Resgrid.Config values.
/// </summary>
public static class AiCacheIsolation
{
    /// <summary>
    /// Null when disabled. Without a usable key (32+ bytes of base64 in <paramref name="key"/> or <paramref name="fallbackKey"/>),
    /// or without a department, every call returns a fresh random salt: no prefix reuse at all, never shared.
    /// </summary>
    public static string? SaltFor(int departmentId, bool enabled, string? key, string? fallbackKey)
    {
        if (!enabled) return null;
        var secret = Key(key) ?? Key(fallbackKey);
        var bytes = secret == null || departmentId <= 0 ? RandomNumberGenerator.GetBytes(16)
            : HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes("resgrid-cache-salt-v1:" + departmentId.ToString(CultureInfo.InvariantCulture))).AsSpan(0, 16).ToArray();
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[]? Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length >= 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

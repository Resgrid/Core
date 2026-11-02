using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Services
{
	/// <summary>
	/// Signed, expiring URLs for server-hosted map resources that native map sources load without an Authorization
	/// header — styles, custom map tiles and images, indoor floor images, static maps, offline manifests and packs
	/// (maplibre-mapping-routing-navigation-plan.md D12). The signature covers the department, a resource scope (e.g.
	/// "custommap:42") and the expiry, never values the client fills in ({z}/{x}/{y} or static-map placeholders), so one
	/// signed template serves every tile. Bearer authentication keeps working on every route that had it (§21).
	/// </summary>
	public static class MapResourceUrls
	{
		public const string DepartmentParameter = "d";
		public const string ExpiresParameter = "e";
		public const string SignatureParameter = "sig";

		public static string StyleScope(string appKey, bool night) => $"style:{appKey ?? string.Empty}:{(night ? "night" : "day")}";
		public static string CustomMapScope(string layerId) => $"custommap:{layerId}";
		public static string IndoorFloorScope(string floorId) => $"indoorfloor:{floorId}";
		public const string StaticMapScope = "staticmap";
		public const string OfflineManifestScope = "offline-manifest";
		public const string OfflineDataScope = "offline-data";
		public static string OfflineAssetScope(string name) => $"offline-asset:{name}";
		public static string OfflinePackScope(string packId) => $"offline-pack:{packId}";

		/// <summary>"d=..&amp;e=..&amp;sig=.." for the scope, valid for the configured number of hours.</summary>
		public static string SignQuery(int departmentId, string scope, TimeSpan? lifetime = null)
		{
			var expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(Math.Max(1, Config.MappingConfig.SignedMapUrlTtlHours)));
			var expiresSeconds = new DateTimeOffset(expires).ToUnixTimeSeconds();
			var signature = Sign(departmentId, scope, expiresSeconds);

			return $"{DepartmentParameter}={departmentId.ToString(CultureInfo.InvariantCulture)}&{ExpiresParameter}={expiresSeconds.ToString(CultureInfo.InvariantCulture)}&{SignatureParameter}={signature}";
		}

		/// <summary>Appends the signed query to a path or URL that may already have a query string.</summary>
		public static string Sign(string url, int departmentId, string scope, TimeSpan? lifetime = null)
		{
			return url + (url.Contains('?') ? "&" : "?") + SignQuery(departmentId, scope, lifetime);
		}

		/// <summary>True when the signature matches the scope and has not expired; returns the signed department.</summary>
		public static bool TryValidate(string departmentText, string expiresText, string signature, string scope, out int departmentId)
		{
			departmentId = 0;

			// Without the passphrase the key is a public constant and anyone could sign; refuse every signed URL instead
			// (bearer callers are unaffected). Same rule as the invoice payment links.
			if (string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase))
				return false;

			if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(scope) ||
			    !int.TryParse(departmentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var department) || department <= 0 ||
			    !long.TryParse(expiresText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresSeconds))
				return false;

			if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresSeconds)
				return false;

			var expected = Sign(department, scope, expiresSeconds);
			if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim())))
				return false;

			departmentId = department;
			return true;
		}

		private static string Sign(int departmentId, string scope, long expiresSeconds)
		{
			var payload = $"map|{departmentId.ToString(CultureInfo.InvariantCulture)}|{scope}|{expiresSeconds.ToString(CultureInfo.InvariantCulture)}";
			using var hmac = new HMACSHA256(Key());
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));

			return Convert.ToBase64String(hash, 0, 24).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		private static byte[] Key()
		{
			// Derived, so a map URL signature can never be replayed as any other signed value made with the passphrase.
			return SHA256.HashData(Encoding.UTF8.GetBytes("resgrid-map-resources|" + (Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase ?? string.Empty)));
		}
	}
}

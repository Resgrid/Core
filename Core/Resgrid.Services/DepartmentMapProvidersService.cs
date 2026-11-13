using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Setting 125 (<see cref="MappingProviderOverrides"/>): a department's own routing, live traffic and geocoding accounts.
	/// Keys are encrypted per department; key derivation is deliberately slow (PBKDF2), so the decrypted form is kept in
	/// process memory and rebuilt only when the stored value changes. Department URLs must be public https addresses:
	/// checked here when saved, and again on every connection by the routing HTTP client.
	/// </summary>
	public class DepartmentMapProvidersService : IDepartmentMapProvidersService
	{
		public const string ApiKeyAuthScheme = MappingConfig.ValhallaApiKeyAuthScheme;
		public const string BearerAuthScheme = MappingConfig.ValhallaBearerAuthScheme;

		private static readonly string[] RoutingProviders =
			{ RoutingProviderNames.Mapbox, RoutingProviderNames.Google, RoutingProviderNames.Valhalla, RoutingProviderNames.Osrm };

		private static readonly string[] GeocodingProviders = { "google", "mapbox", "locationiq" };

		private static readonly ConcurrentDictionary<int, (string Fingerprint, DepartmentMapProviders Value)> Decrypted =
			new ConcurrentDictionary<int, (string, DepartmentMapProviders)>();

		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEncryptionService _encryptionService;

		public DepartmentMapProvidersService(IDepartmentSettingsService departmentSettingsService, IDepartmentsService departmentsService,
			IEncryptionService encryptionService)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_encryptionService = encryptionService;
		}

		public async Task<DepartmentMapProviders> GetAsync(int departmentId)
		{
			if (departmentId <= 0)
				return DepartmentMapProviders.None;

			var stored = await _departmentSettingsService.GetMappingProviderOverridesAsync(departmentId);
			if (stored == null || stored.IsEmpty)
			{
				Decrypted.TryRemove(departmentId, out _);
				return DepartmentMapProviders.None;
			}

			var fingerprint = Fingerprint(stored);
			if (Decrypted.TryGetValue(departmentId, out var known) && known.Fingerprint == fingerprint)
				return known.Value;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return DepartmentMapProviders.None;

			var value = Decrypt(stored, departmentId, department.Code);
			Decrypted[departmentId] = (fingerprint, value);
			return value;
		}

		public async Task<DepartmentMapProvidersSaveResult> SaveAsync(int departmentId, DepartmentMapProvidersUpdate update,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = new DepartmentMapProvidersSaveResult();
			update ??= new DepartmentMapProvidersUpdate();

			var existing = await _departmentSettingsService.GetMappingProviderOverridesAsync(departmentId, true) ?? new MappingProviderOverrides();
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				throw new InvalidOperationException($"Department {departmentId} not found");

			string Encrypt(string plain) => _encryptionService.EncryptForDepartment(plain.Trim(), departmentId, department.Code);

			var next = new MappingProviderOverrides();

			// ---- Routing ----
			var routing = Normalize(update.RoutingProvider);
			if (!string.IsNullOrEmpty(routing))
			{
				if (!RoutingProviders.Contains(routing))
				{
					result.Errors.Add(DepartmentMapProvidersError.UnknownRoutingProvider);
				}
				else
				{
					next.RoutingProvider = routing;
					var sameProvider = routing == Normalize(existing.RoutingProvider);
					var newKey = update.RoutingApiKey?.Trim();

					if (!string.IsNullOrEmpty(newKey))
						next.RoutingApiKeyEncrypted = Encrypt(newKey);
					else if (sameProvider && !update.ClearRoutingApiKey)
						next.RoutingApiKeyEncrypted = existing.RoutingApiKeyEncrypted;

					var keyRequired = routing == RoutingProviderNames.Mapbox || routing == RoutingProviderNames.Google;
					if (keyRequired && string.IsNullOrEmpty(next.RoutingApiKeyEncrypted))
						result.Errors.Add(DepartmentMapProvidersError.RoutingKeyRequired);

					if (routing == RoutingProviderNames.Valhalla || routing == RoutingProviderNames.Osrm)
					{
						var url = update.RoutingUrl?.Trim();
						if (string.IsNullOrEmpty(url))
							result.Errors.Add(DepartmentMapProvidersError.RoutingUrlRequired);
						else if (!await IsPublicHttpsUrlAsync(url, cancellationToken))
							result.Errors.Add(DepartmentMapProvidersError.RoutingUrlNotPublic);
						else
							next.RoutingUrl = url.TrimEnd('/');
					}

					if (routing == RoutingProviderNames.Valhalla)
						next.RoutingAuthScheme = string.Equals(update.RoutingAuthScheme?.Trim(), BearerAuthScheme, StringComparison.OrdinalIgnoreCase)
							? BearerAuthScheme
							: ApiKeyAuthScheme;

					if (routing == RoutingProviderNames.Google)
						next.GoogleRoutingApi = string.Equals(update.GoogleRoutingApi?.Trim(), "routes", StringComparison.OrdinalIgnoreCase) ? "routes" : "legacy";
				}
			}

			// ---- Live traffic ----
			var traffic = Normalize(update.TrafficSource);
			if (traffic == MappingProviderOverrides.TrafficSourceMapbox)
			{
				next.TrafficSource = traffic;
				var token = update.TrafficMapboxToken?.Trim();
				if (string.IsNullOrEmpty(token) && Normalize(existing.TrafficSource) == traffic)
					token = existing.TrafficMapboxToken;

				if (!MappingConfig.IsPublicMapboxAccessToken(token))
					result.Errors.Add(DepartmentMapProvidersError.TrafficMapboxTokenInvalid);
				else
					next.TrafficMapboxToken = token;
			}
			else if (traffic == MappingProviderOverrides.TrafficSourceTiles)
			{
				next.TrafficSource = traffic;
				var url = update.TrafficTileUrl?.Trim();
				if (!string.IsNullOrEmpty(url))
				{
					if (IsTileUrlTemplate(url))
						next.TrafficTileUrlEncrypted = Encrypt(url);
					else
						result.Errors.Add(DepartmentMapProvidersError.TrafficTileUrlInvalid);
				}
				else if (Normalize(existing.TrafficSource) == traffic && !string.IsNullOrEmpty(existing.TrafficTileUrlEncrypted))
				{
					next.TrafficTileUrlEncrypted = existing.TrafficTileUrlEncrypted;
				}
				else
				{
					result.Errors.Add(DepartmentMapProvidersError.TrafficTileUrlInvalid);
				}
			}
			else if (!string.IsNullOrEmpty(traffic))
			{
				result.Errors.Add(DepartmentMapProvidersError.UnknownTrafficSource);
			}

			// ---- Geocoding ----
			var geocoding = Normalize(update.GeocodingProvider);
			if (!string.IsNullOrEmpty(geocoding))
			{
				if (!GeocodingProviders.Contains(geocoding))
				{
					result.Errors.Add(DepartmentMapProvidersError.UnknownGeocodingProvider);
				}
				else
				{
					next.GeocodingProvider = geocoding;
					var key = update.GeocodingApiKey?.Trim();
					if (!string.IsNullOrEmpty(key))
						next.GeocodingApiKeyEncrypted = Encrypt(key);
					else if (geocoding == Normalize(existing.GeocodingProvider))
						next.GeocodingApiKeyEncrypted = existing.GeocodingApiKeyEncrypted;

					if (string.IsNullOrEmpty(next.GeocodingApiKeyEncrypted))
						result.Errors.Add(DepartmentMapProvidersError.GeocodingKeyRequired);
				}
			}

			if (result.Errors.Count > 0)
				return result;

			await _departmentSettingsService.SetMappingProviderOverridesAsync(departmentId, next, cancellationToken);
			Decrypted.TryRemove(departmentId, out _);
			result.Saved = true;
			return result;
		}

		/// <summary>An https URL template with {z}, {x} and {y}; the browser fetches it, so it must not be plain http.</summary>
		public static bool IsTileUrlTemplate(string url)
		{
			if (string.IsNullOrWhiteSpace(url) || !url.Contains("{z}") || !url.Contains("{x}") || !url.Contains("{y}"))
				return false;

			var probe = url.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0");
			return Uri.TryCreate(probe, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
		}

		/// <summary>https, no user info, and every address of the host public (literal IPs included).</summary>
		public static async Task<bool> IsPublicHttpsUrlAsync(string url, CancellationToken cancellationToken)
		{
			if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			    !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
				return false;

			if (IPAddress.TryParse(uri.IdnHost, out var literal))
				return PublicNetworkAddress.IsPublic(literal);

			try
			{
				var addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, cancellationToken);
				return addresses.Length > 0 && addresses.All(PublicNetworkAddress.IsPublic);
			}
			catch (Exception)
			{
				return false;
			}
		}

		private DepartmentMapProviders Decrypt(MappingProviderOverrides stored, int departmentId, string departmentCode)
		{
			string Plain(string cipher)
			{
				if (string.IsNullOrWhiteSpace(cipher))
					return null;

				try
				{
					return _encryptionService.DecryptForDepartment(cipher, departmentId, departmentCode);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not decrypt department {departmentId}'s map provider key; the system default is used");
					return null;
				}
			}

			var value = new DepartmentMapProviders();

			var routing = Normalize(stored.RoutingProvider);
			if (!string.IsNullOrEmpty(routing))
			{
				value.Routing = new RoutingProviderCredentials
				{
					DepartmentId = departmentId,
					Provider = routing,
					Url = stored.RoutingUrl,
					ApiKey = Plain(stored.RoutingApiKeyEncrypted),
					AuthScheme = stored.RoutingAuthScheme,
					GoogleRoutingApi = stored.GoogleRoutingApi
				};
			}

			var geocoding = Normalize(stored.GeocodingProvider);
			if (!string.IsNullOrEmpty(geocoding))
			{
				var key = Plain(stored.GeocodingApiKeyEncrypted);
				if (!string.IsNullOrEmpty(key))
				{
					value.GeocodingProvider = geocoding;
					value.GeocodingApiKey = key;
				}
			}

			var traffic = Normalize(stored.TrafficSource);
			if (traffic == MappingProviderOverrides.TrafficSourceMapbox && MappingConfig.IsPublicMapboxAccessToken(stored.TrafficMapboxToken))
			{
				value.Traffic = new TrafficLayerSource
				{
					Url = MapStyleService.MapboxTrafficTileUrl(stored.TrafficMapboxToken),
					Type = TrafficLayerSource.VectorType,
					IsDepartment = true
				};
			}
			else if (traffic == MappingProviderOverrides.TrafficSourceTiles)
			{
				var url = Plain(stored.TrafficTileUrlEncrypted);
				if (IsTileUrlTemplate(url))
					value.Traffic = new TrafficLayerSource { Url = url, Type = TrafficLayerSource.RasterType, IsDepartment = true };
			}

			return value;
		}

		private static string Fingerprint(MappingProviderOverrides stored)
		{
			return string.Join("\u001f", stored.RoutingProvider, stored.RoutingUrl, stored.RoutingApiKeyEncrypted, stored.RoutingAuthScheme,
				stored.GoogleRoutingApi, stored.TrafficSource, stored.TrafficMapboxToken, stored.TrafficTileUrlEncrypted, stored.GeocodingProvider,
				stored.GeocodingApiKeyEncrypted);
		}

		private static string Normalize(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
		}
	}
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records.Connectors
{
	/// <summary>
	/// Reads the Resgrid Mutual-Aid Order Feed over https (RMS plan section 4.1). One shared client, a bounded
	/// timeout and a bounded body: a slow or enormous source cannot hold a worker or exhaust memory. The
	/// credential travels only as the connector says it should — a bearer token or a named header — and is
	/// never logged.
	/// </summary>
	public abstract class ExternalOrderFeedProviderBase : IExternalOrderFeedProvider
	{
		/// <summary>
		/// Redirects are not followed: the destination is checked against its resolved addresses before the
		/// request, and a redirect the handler follows on its own would go somewhere nothing checked. A source
		/// that answers 3xx is reported as an error so its administrator fixes the feed root instead.
		/// </summary>
		private static readonly HttpClient SharedHttpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
		{
			Timeout = TimeSpan.FromSeconds(Math.Max(5, RecordsConnectorConfig.TimeoutSeconds))
		};

		private readonly HttpClient _http;

		protected ExternalOrderFeedProviderBase(HttpClient http = null)
		{
			_http = http ?? SharedHttpClient;
		}

		public abstract string Key { get; }
		public abstract string DefaultScheme { get; }
		public abstract string DefaultProfileKey { get; }

		public virtual async Task<string> FetchAsync(RmsExternalOrderConnector connector, string credential, string cursor, CancellationToken cancellationToken = default)
		{
			if (connector == null) throw new ArgumentNullException(nameof(connector));
			var root = (connector.BaseUrl ?? string.Empty).Trim();
			if (!Uri.TryCreate(root, UriKind.Absolute, out var uri))
				throw new InvalidOperationException("The connector has no usable feed root.");
			if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) && !(RecordsConnectorConfig.AllowHttp && string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException("The feed root must be https.");

			// Checked again here, not only when the connector was saved: the allowlist can have changed, and a name
			// that resolved publicly then can resolve to something internal now.
			await ExternalFeedDestination.RequireAllowedDestinationAsync(uri, cancellationToken);

			var builder = new UriBuilder(uri);
			if (!string.IsNullOrWhiteSpace(cursor))
			{
				var query = builder.Query.TrimStart('?');
				builder.Query = (query.Length > 0 ? query + "&" : string.Empty) + "cursor=" + Uri.EscapeDataString(cursor);
			}

			using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.TryAddWithoutValidation("X-Resgrid-Feed-Contract", ExternalOrderFeedContract.Version);
			switch ((connector.CredentialKind ?? RmsConnectorCredentialKinds.None).ToLowerInvariant())
			{
				case RmsConnectorCredentialKinds.Bearer:
					if (!string.IsNullOrEmpty(credential)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
					break;
				case RmsConnectorCredentialKinds.Header:
					if (!string.IsNullOrEmpty(credential) && !string.IsNullOrWhiteSpace(connector.CredentialHeaderName)) request.Headers.TryAddWithoutValidation(connector.CredentialHeaderName.Trim(), credential);
					break;
			}

			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException($"The source answered {(int)response.StatusCode} {response.ReasonPhrase}.");
			if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > RecordsConnectorConfig.MaxFeedBytes)
				throw new InvalidOperationException($"The feed page is larger than the {RecordsConnectorConfig.MaxFeedBytes / (1024 * 1024)} MB limit.");

			using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using var buffer = new System.IO.MemoryStream();
			var chunk = new byte[64 * 1024];
			int read;
			while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)) > 0)
			{
				buffer.Write(chunk, 0, read);
				if (buffer.Length > RecordsConnectorConfig.MaxFeedBytes)
					throw new InvalidOperationException($"The feed page is larger than the {RecordsConnectorConfig.MaxFeedBytes / (1024 * 1024)} MB limit.");
			}
			return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
		}

		public virtual List<string> ValidateOrder(ExternalOrderFeedOrder order) => new List<string>();
	}

	/// <summary>Any all-hazard or local mutual-aid system: opaque identifiers, no scheme-specific demands.</summary>
	public sealed class GenericOrderFeedProvider : ExternalOrderFeedProviderBase
	{
		public GenericOrderFeedProvider(HttpClient http = null) : base(http) { }
		public override string Key => RmsExternalOrderConnectorProviders.Generic;
		public override string DefaultScheme => "local";
		public override string DefaultProfileKey => RmsDeploymentProfiles.LocalMutualAid;
	}

	/// <summary>A member agency's own ordering system under its own opaque scheme.</summary>
	public sealed class AgencyOrderFeedProvider : ExternalOrderFeedProviderBase
	{
		public AgencyOrderFeedProvider(HttpClient http = null) : base(http) { }
		public override string Key => RmsExternalOrderConnectorProviders.Agency;
		public override string DefaultScheme => "agency";
		public override string DefaultProfileKey => RmsDeploymentProfiles.Generic;
	}

	/// <summary>
	/// IROC-shaped U.S. wildland feed. IROC is the ordering authority; an order without a resource-order number
	/// and a request without a request number are not IROC facts and are refused rather than provisioned.
	/// </summary>
	public sealed class IrocOrderFeedProvider : ExternalOrderFeedProviderBase
	{
		public IrocOrderFeedProvider(HttpClient http = null) : base(http) { }
		public override string Key => RmsExternalOrderConnectorProviders.Iroc;
		public override string DefaultScheme => "iroc";
		public override string DefaultProfileKey => RmsDeploymentProfiles.UsWildland;

		public override List<string> ValidateOrder(ExternalOrderFeedOrder order)
		{
			var problems = new List<string>();
			if (order == null) return problems;
			if (string.IsNullOrWhiteSpace(order.IncidentNumber)) problems.Add("IROC orders carry an incident number.");
			if (string.IsNullOrWhiteSpace(order.OrderingOffice) && string.IsNullOrWhiteSpace(order.DispatchOffice)) problems.Add("IROC orders name an ordering or dispatch office.");
			foreach (var request in order.Requests ?? new List<ExternalOrderFeedRequest>())
			{
				if (string.IsNullOrWhiteSpace(request?.Category)) problems.Add($"Request {request?.RequestNumber} has no category (overhead, crew, equipment, aircraft, supply).");
			}
			return problems;
		}
	}

	/// <summary>
	/// CIFFC / member-agency Canadian exchange under MARS. The agency and the exchange identifier are what make
	/// a request traceable to its agreement, so both are mandatory.
	/// </summary>
	public sealed class CiffcOrderFeedProvider : ExternalOrderFeedProviderBase
	{
		public CiffcOrderFeedProvider(HttpClient http = null) : base(http) { }
		public override string Key => RmsExternalOrderConnectorProviders.Ciffc;
		public override string DefaultScheme => "ciffc";
		public override string DefaultProfileKey => RmsDeploymentProfiles.CaWildland;

		public override List<string> ValidateOrder(ExternalOrderFeedOrder order)
		{
			var problems = new List<string>();
			if (order == null) return problems;
			if (string.IsNullOrWhiteSpace(order.RequestingAgency) && string.IsNullOrWhiteSpace(order.ReceivingAgency)) problems.Add("CIFFC exchanges name a requesting or receiving agency.");
			if (string.IsNullOrWhiteSpace(order.AgreementReference)) problems.Add("CIFFC exchanges carry the MARS agreement or exchange reference.");
			return problems;
		}
	}
}

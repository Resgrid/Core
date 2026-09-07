using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// An order feed that serves whatever pages the test queued, keyed by cursor, and counts every fetch. No HTTP:
	/// the provider contract is what is under test, and the real providers differ only in scheme, profile and
	/// validation, which a test can pass in.
	/// </summary>
	public sealed class FakeOrderFeedProvider : IExternalOrderFeedProvider
	{
		private readonly Func<ExternalOrderFeedOrder, List<string>> _validate;
		private readonly FakeOrderFeedProvider _pagesFrom;
		private readonly Dictionary<string, string> _pages = new Dictionary<string, string>(StringComparer.Ordinal);

		public FakeOrderFeedProvider() : this(RmsExternalOrderConnectorProviders.Generic, "local", RmsDeploymentProfiles.LocalMutualAid, null) { }

		/// <param name="pagesFrom">Another fake whose queued pages and counters this one shares, so a test serves one feed whatever provider the connector names.</param>
		public FakeOrderFeedProvider(string key, string scheme, string profile, Func<ExternalOrderFeedOrder, List<string>> validate, FakeOrderFeedProvider pagesFrom = null)
		{
			Key = key;
			DefaultScheme = scheme;
			DefaultProfileKey = profile;
			_validate = validate;
			_pagesFrom = pagesFrom;
		}

		public string Key { get; }
		public string DefaultScheme { get; }
		public string DefaultProfileKey { get; }

		public int Fetches { get; private set; }
		public List<string> CursorsSeen { get; } = new List<string>();
		public Exception Throw { get; set; }

		/// <summary>The page served when the connector has no cursor (or the cursor the test names).</summary>
		public void Serve(ExternalOrderFeed feed, string forCursor = null) => _pages[forCursor ?? string.Empty] = JsonConvert.SerializeObject(feed);

		public void ServeRaw(string json, string forCursor = null) => _pages[forCursor ?? string.Empty] = json;

		public Task<string> FetchAsync(RmsExternalOrderConnector connector, string credential, string cursor, CancellationToken cancellationToken = default)
		{
			if (_pagesFrom != null) return _pagesFrom.FetchAsync(connector, credential, cursor, cancellationToken);
			Fetches++;
			CursorsSeen.Add(cursor);
			LastCredential = credential;
			if (Throw != null) throw Throw;
			return Task.FromResult(_pages.TryGetValue(cursor ?? string.Empty, out var page) ? page : _pages.Values.FirstOrDefault() ?? "{}");
		}

		public string LastCredential { get; private set; }

		public List<string> ValidateOrder(ExternalOrderFeedOrder order) => _validate?.Invoke(order) ?? new List<string>();

		public static ExternalOrderFeed Feed(string version, params ExternalOrderFeedOrder[] orders) => new ExternalOrderFeed
		{
			Contract = ExternalOrderFeedContract.Version,
			Source = new ExternalOrderFeedSource { System = "Test Ordering", Scheme = "local", Version = version, GeneratedOn = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero) },
			Orders = orders.ToList()
		};

		public static ExternalOrderFeedOrder Order(string number, string status = "open", params ExternalOrderFeedRequest[] requests) => new ExternalOrderFeedOrder
		{
			OrderNumber = number, IncidentName = "Bear Creek", IncidentNumber = "OR-UPF-000123", IncidentCountry = "US", IncidentSubdivision = "OR", OrderingOffice = "ORCOC", DispatchOffice = "Central Oregon",
			RequestingAgency = "USFS", SendingAgency = "Test County Fire", CostCode = "P4NABC", AgreementReference = "MA-2026-01", Status = status, CapturedOn = new DateTimeOffset(2026, 9, 6, 11, 0, 0, TimeSpan.Zero),
			Requests = requests.ToList(), Artifact = new ExternalOrderFeedArtifact { Url = "https://orders.example.gov/" + number, ContentType = "application/pdf" }
		};

		public static ExternalOrderFeedRequest Request(string number, string status = "requested", string category = "overhead") => new ExternalOrderFeedRequest
		{
			RequestNumber = number, Category = category, ResourceKind = category == "equipment" ? "unit" : "person", ResourceType = category == "equipment" ? "Engine T3" : null, Position = category == "equipment" ? "ENGB" : "DIVS",
			Status = status, NeededOn = new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero)
		};
	}
}

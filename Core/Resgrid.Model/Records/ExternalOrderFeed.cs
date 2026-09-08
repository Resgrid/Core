using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// The Resgrid Mutual-Aid Order Feed, contract v1 (RMS plan section 4.1). This is the documented API a
	/// connector reads: an ordering system, or the integration middleware in front of it, publishes its orders,
	/// requests and fills in this shape. Resgrid never scrapes a screen and never infers an order update — what
	/// is not in this feed did not happen as far as import is concerned. Identifiers are opaque and
	/// source-qualified; the source stays authoritative for what was ordered, the department for what it did.
	/// </summary>
	public static class ExternalOrderFeedContract
	{
		public const string Version = "resgrid.mutual-aid-order-feed.v1";

		/// <summary>Most orders one feed page may carry; larger sources page with the cursor.</summary>
		public const int MaxOrdersPerPage = 200;

		/// <summary>Most requests one order may carry in a page.</summary>
		public const int MaxRequestsPerOrder = 500;

		/// <summary>Order statuses the feed may declare.</summary>
		public static class OrderStatuses
		{
			public const string Open = "open";
			public const string Mobilized = "mobilized";
			public const string Released = "released";
			public const string Closed = "closed";
			public static readonly IReadOnlyList<string> All = new[] { Open, Mobilized, Released, Closed };
		}

		/// <summary>Request statuses the feed may declare, in lifecycle order.</summary>
		public static class RequestStatuses
		{
			public const string Requested = "requested";
			public const string Filled = "filled";
			public const string Mobilized = "mobilized";
			public const string CheckedIn = "checked-in";
			public const string Assigned = "assigned";
			public const string Released = "released";
			public const string Demobilized = "demobilized";
			public const string Cancelled = "cancelled";
			public static readonly IReadOnlyList<string> All = new[] { Requested, Filled, Mobilized, CheckedIn, Assigned, Released, Demobilized, Cancelled };

			/// <summary>Lifecycle rank so source and local states can be compared; cancelled is outside the ladder.</summary>
			public static int Rank(string status)
			{
				switch ((status ?? string.Empty).Trim().ToLowerInvariant())
				{
					case Requested: return 1;
					case Filled: return 2;
					case Mobilized: return 4;
					case CheckedIn: return 5;
					case Assigned: return 6;
					case Released: return 7;
					case Demobilized: return 8;
					default: return 0;
				}
			}
		}

		/// <summary>The local fill status expressed on the feed's ladder, for reconciliation.</summary>
		public static string LocalStatusOf(RmsDeploymentFillStatus status)
		{
			switch (status)
			{
				case RmsDeploymentFillStatus.Requested: return RequestStatuses.Requested;
				case RmsDeploymentFillStatus.Accepted: return RequestStatuses.Filled;
				case RmsDeploymentFillStatus.Declined: return RequestStatuses.Cancelled;
				case RmsDeploymentFillStatus.Mobilized: return RequestStatuses.Mobilized;
				case RmsDeploymentFillStatus.CheckedIn: return RequestStatuses.CheckedIn;
				case RmsDeploymentFillStatus.Assigned: return RequestStatuses.Assigned;
				case RmsDeploymentFillStatus.Released: return RequestStatuses.Released;
				case RmsDeploymentFillStatus.Demobilized: return RequestStatuses.Demobilized;
				case RmsDeploymentFillStatus.Returned: return "returned";
				default: return RequestStatuses.Requested;
			}
		}

		/// <summary>
		/// A value the source controls, made safe to repeat back. A parse problem is stored on the run and shown
		/// to an administrator, so a source cannot use it to put arbitrary text or length in front of a person:
		/// what comes back is a short token of plain characters, or a placeholder.
		/// </summary>
		private static string Safe(string sourceValue)
		{
			if (string.IsNullOrWhiteSpace(sourceValue))
				return "(none)";
			var kept = new string(sourceValue.Trim().Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_').Take(32).ToArray());
			return kept.Length == 0 ? "(unreadable)" : kept;
		}

		/// <summary>Parses and validates one feed document. Problems are returned, never thrown, so a run can log them.</summary>
		public static ExternalOrderFeed Parse(string json, out List<string> problems)
		{
			problems = new List<string>();
			if (string.IsNullOrWhiteSpace(json))
			{
				problems.Add("The feed is empty.");
				return null;
			}

			ExternalOrderFeed feed;
			try
			{
				feed = JsonConvert.DeserializeObject<ExternalOrderFeed>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.DateTimeOffset });
			}
			catch (JsonException ex)
			{
				problems.Add("The feed is not valid JSON: " + ex.Message);
				return null;
			}

			if (feed == null)
			{
				problems.Add("The feed is empty.");
				return null;
			}
			if (!string.Equals(feed.Contract, Version, StringComparison.Ordinal))
			{
				problems.Add($"The feed declares contract '{Safe(feed.Contract)}'; this connector speaks '{Version}'.");
				return null;
			}
			feed.Orders ??= new List<ExternalOrderFeedOrder>();
			if (feed.Orders.Count > MaxOrdersPerPage)
			{
				problems.Add($"The feed carries {feed.Orders.Count} orders in one page; the contract allows {MaxOrdersPerPage}. Page with the cursor.");
				return null;
			}

			for (var i = 0; i < feed.Orders.Count; i++)
			{
				var order = feed.Orders[i];
				var label = $"orders[{i}]";
				if (order == null) { problems.Add(label + " is null."); continue; }
				if (string.IsNullOrWhiteSpace(order.OrderNumber)) problems.Add(label + ".orderNumber is required.");
				if (string.IsNullOrWhiteSpace(order.IncidentName)) problems.Add(label + ".incidentName is required.");
				if (!string.IsNullOrWhiteSpace(order.Status) && !OrderStatuses.All.Contains(order.Status.Trim().ToLowerInvariant()))
					problems.Add($"{label}.status '{order.Status}' is not one of {string.Join(", ", OrderStatuses.All)}.");
				order.Requests ??= new List<ExternalOrderFeedRequest>();
				if (order.Requests.Count > MaxRequestsPerOrder)
					problems.Add($"{label} carries {order.Requests.Count} requests; the contract allows {MaxRequestsPerOrder}.");
				for (var j = 0; j < order.Requests.Count; j++)
				{
					var request = order.Requests[j];
					if (request == null) { problems.Add($"{label}.requests[{j}] is null."); continue; }
					if (string.IsNullOrWhiteSpace(request.RequestNumber)) problems.Add($"{label}.requests[{j}].requestNumber is required.");
					if (!string.IsNullOrWhiteSpace(request.Status) && !RequestStatuses.All.Contains(request.Status.Trim().ToLowerInvariant()))
						problems.Add($"{label}.requests[{j}].status '{request.Status}' is not one of {string.Join(", ", RequestStatuses.All)}.");
				}
				if (order.Artifact != null && !string.IsNullOrWhiteSpace(order.Artifact.Url) && !order.Artifact.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
					problems.Add($"{label}.artifact.url must be https.");
			}

			return problems.Count == 0 ? feed : null;
		}
	}

	public class ExternalOrderFeed
	{
		[JsonProperty("contract")]
		public string Contract { get; set; }

		[JsonProperty("source")]
		public ExternalOrderFeedSource Source { get; set; }

		/// <summary>Opaque resume cursor; the next request sends it back. Null means the page is complete.</summary>
		[JsonProperty("cursor")]
		public string Cursor { get; set; }

		[JsonProperty("orders")]
		public List<ExternalOrderFeedOrder> Orders { get; set; } = new List<ExternalOrderFeedOrder>();
	}

	public class ExternalOrderFeedSource
	{
		[JsonProperty("system")]
		public string System { get; set; }

		[JsonProperty("scheme")]
		public string Scheme { get; set; }

		[JsonProperty("version")]
		public string Version { get; set; }

		[JsonProperty("generatedOn")]
		public DateTimeOffset? GeneratedOn { get; set; }
	}

	public class ExternalOrderFeedOrder
	{
		[JsonProperty("orderNumber")]
		public string OrderNumber { get; set; }

		[JsonProperty("incidentName")]
		public string IncidentName { get; set; }

		[JsonProperty("incidentNumber")]
		public string IncidentNumber { get; set; }

		[JsonProperty("incidentCountry")]
		public string IncidentCountry { get; set; }

		[JsonProperty("incidentSubdivision")]
		public string IncidentSubdivision { get; set; }

		[JsonProperty("orderingOffice")]
		public string OrderingOffice { get; set; }

		[JsonProperty("dispatchOffice")]
		public string DispatchOffice { get; set; }

		[JsonProperty("requestingAgency")]
		public string RequestingAgency { get; set; }

		[JsonProperty("receivingAgency")]
		public string ReceivingAgency { get; set; }

		[JsonProperty("sendingAgency")]
		public string SendingAgency { get; set; }

		[JsonProperty("costCode")]
		public string CostCode { get; set; }

		[JsonProperty("agreementReference")]
		public string AgreementReference { get; set; }

		[JsonProperty("currencyCode")]
		public string CurrencyCode { get; set; }

		[JsonProperty("measurementSystem")]
		public string MeasurementSystem { get; set; }

		[JsonProperty("timeZoneId")]
		public string TimeZoneId { get; set; }

		/// <summary>The source's own version of this order; a change here is what makes a new snapshot.</summary>
		[JsonProperty("sourceVersion")]
		public string SourceVersion { get; set; }

		[JsonProperty("capturedOn")]
		public DateTimeOffset? CapturedOn { get; set; }

		[JsonProperty("status")]
		public string Status { get; set; }

		[JsonProperty("requests")]
		public List<ExternalOrderFeedRequest> Requests { get; set; } = new List<ExternalOrderFeedRequest>();

		[JsonProperty("artifact")]
		public ExternalOrderFeedArtifact Artifact { get; set; }
	}

	public class ExternalOrderFeedRequest
	{
		[JsonProperty("requestNumber")]
		public string RequestNumber { get; set; }

		[JsonProperty("parentRequestNumber")]
		public string ParentRequestNumber { get; set; }

		[JsonProperty("category")]
		public string Category { get; set; }

		[JsonProperty("fillNumber")]
		public string FillNumber { get; set; }

		[JsonProperty("resourceKind")]
		public string ResourceKind { get; set; }

		[JsonProperty("resourceType")]
		public string ResourceType { get; set; }

		[JsonProperty("resourceTypeScheme")]
		public string ResourceTypeScheme { get; set; }

		[JsonProperty("position")]
		public string Position { get; set; }

		[JsonProperty("positionScheme")]
		public string PositionScheme { get; set; }

		[JsonProperty("isTrainee")]
		public bool IsTrainee { get; set; }

		[JsonProperty("homeUnit")]
		public string HomeUnit { get; set; }

		[JsonProperty("hostAgency")]
		public string HostAgency { get; set; }

		[JsonProperty("agencyUnitId")]
		public string AgencyUnitId { get; set; }

		[JsonProperty("pointOfHire")]
		public string PointOfHire { get; set; }

		[JsonProperty("costCode")]
		public string CostCode { get; set; }

		[JsonProperty("agreementReference")]
		public string AgreementReference { get; set; }

		[JsonProperty("requestedOn")]
		public DateTimeOffset? RequestedOn { get; set; }

		[JsonProperty("neededOn")]
		public DateTimeOffset? NeededOn { get; set; }

		[JsonProperty("status")]
		public string Status { get; set; }
	}

	public class ExternalOrderFeedArtifact
	{
		/// <summary>An https link to the source's own artifact. Kept as a reference only; never fetched or executed.</summary>
		[JsonProperty("url")]
		public string Url { get; set; }

		[JsonProperty("contentType")]
		public string ContentType { get; set; }
	}
}

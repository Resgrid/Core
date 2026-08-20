using System;

using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class WeatherAlertResultData
	{
		public string WeatherAlertId { get; set; }
		public int DepartmentId { get; set; }
		public string WeatherAlertSourceId { get; set; }
		public string ExternalId { get; set; }
		public string Sender { get; set; }
		public string Event { get; set; }
		public int AlertCategory { get; set; }
		public int Severity { get; set; }
		public int Urgency { get; set; }
		public int Certainty { get; set; }
		public int Status { get; set; }
		public string Headline { get; set; }
		public string Description { get; set; }
		public string Instruction { get; set; }
		public string AreaDescription { get; set; }
		public string Polygon { get; set; }
		public string Geocodes { get; set; }
		public string CenterGeoLocation { get; set; }
		// Despite the names, these string fields carry the department-local DISPLAY format
		// ("MM/dd/yyyy h:mm:ss tt"). Deployed app builds render them verbatim, so the format cannot
		// change. Clients doing date math must use the *OnUtc instants below instead.
		public string OnsetUtc { get; set; }
		public string ExpiresUtc { get; set; }
		public string EffectiveUtc { get; set; }
		public string SentUtc { get; set; }

		/// <summary>Actual UTC instant the alert became effective, serialised with an explicit "Z".</summary>
		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime EffectiveOnUtc { get; set; }

		/// <summary>Actual UTC instant the alert expires, serialised with an explicit "Z".</summary>
		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? ExpiresOnUtc { get; set; }

		/// <summary>Actual UTC onset instant, serialised with an explicit "Z".</summary>
		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? OnsetOnUtc { get; set; }

		/// <summary>Actual UTC instant the alert was sent, serialised with an explicit "Z".</summary>
		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? SentOnUtc { get; set; }
		public string FirstSeenUtc { get; set; }
		public string LastUpdatedUtc { get; set; }
		public string ReferencesExternalId { get; set; }
		public bool NotificationSent { get; set; }
		public int? SystemMessageId { get; set; }
	}
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("WeatherAlerts")]
	public class WeatherAlert : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid WeatherAlertId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[ForeignKey("WeatherAlertSource"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public Guid WeatherAlertSourceId { get; set; }

		public virtual WeatherAlertSource WeatherAlertSource { get; set; }

		[MaxLength(500)]
		public string ExternalId { get; set; }

		[MaxLength(500)]
		public string Sender { get; set; }

		[MaxLength(500)]
		public string Event { get; set; }

		public int AlertCategory { get; set; }

		public int Severity { get; set; }

		public int Urgency { get; set; }

		public int Certainty { get; set; }

		public int Status { get; set; }

		[MaxLength(500)]
		public string Headline { get; set; }

		public string Description { get; set; }

		public string Instruction { get; set; }

		[MaxLength(500)]
		public string AreaDescription { get; set; }

		public string Polygon { get; set; }

		public string Geocodes { get; set; }

		[MaxLength(100)]
		public string CenterGeoLocation { get; set; }

		public DateTime? OnsetUtc { get; set; }

		public DateTime? ExpiresUtc { get; set; }

		public DateTime EffectiveUtc { get; set; }

		public DateTime? SentUtc { get; set; }

		public DateTime FirstSeenUtc { get; set; }

		public DateTime LastUpdatedUtc { get; set; }

		[MaxLength(500)]
		public string ReferencesExternalId { get; set; }

		public bool NotificationSent { get; set; }

		public int? SystemMessageId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WeatherAlertId == Guid.Empty ? null : (object)WeatherAlertId.ToString(); }
			set { WeatherAlertId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "WeatherAlerts";

		[NotMapped]
		public string IdName => "WeatherAlertId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "WeatherAlertSource" };
	}
}

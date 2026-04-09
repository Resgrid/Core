using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("WeatherAlertSources")]
	public class WeatherAlertSource : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid WeatherAlertSourceId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[MaxLength(200)]
		public string Name { get; set; }

		public int SourceType { get; set; }

		[MaxLength(1000)]
		public string AreaFilter { get; set; }

		[MaxLength(500)]
		public string ApiKey { get; set; }

		[MaxLength(2000)]
		public string CustomEndpoint { get; set; }

		public int PollIntervalMinutes { get; set; }

		public bool Active { get; set; }

		public DateTime? LastPollUtc { get; set; }

		public DateTime? LastSuccessUtc { get; set; }

		public bool IsFailure { get; set; }

		[MaxLength(2000)]
		public string ErrorMessage { get; set; }

		[MaxLength(500)]
		public string LastETag { get; set; }

		public DateTime CreatedOn { get; set; }

		[MaxLength(128)]
		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WeatherAlertSourceId == Guid.Empty ? null : (object)WeatherAlertSourceId.ToString(); }
			set { WeatherAlertSourceId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "WeatherAlertSources";

		[NotMapped]
		public string IdName => "WeatherAlertSourceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "ContactEmail" };

		/// <summary>
		/// Non-persisted. Populated by the service layer with the department admin's email
		/// for use as contact info in upstream API User-Agent headers.
		/// </summary>
		[NotMapped]
		public string ContactEmail { get; set; }
	}
}

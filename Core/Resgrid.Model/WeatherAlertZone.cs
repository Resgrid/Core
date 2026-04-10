using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("WeatherAlertZones")]
	public class WeatherAlertZone : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid WeatherAlertZoneId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[MaxLength(200)]
		public string Name { get; set; }

		[MaxLength(100)]
		public string ZoneCode { get; set; }

		[MaxLength(100)]
		public string CenterGeoLocation { get; set; }

		public double RadiusMiles { get; set; }

		public bool IsActive { get; set; }

		public bool IsPrimary { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WeatherAlertZoneId == Guid.Empty ? null : (object)WeatherAlertZoneId.ToString(); }
			set { WeatherAlertZoneId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "WeatherAlertZones";

		[NotMapped]
		public string IdName => "WeatherAlertZoneId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}

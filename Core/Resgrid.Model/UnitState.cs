using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	[Table("UnitStates")]
	public class UnitState : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitStateId { get; set; }

		[Required]
		public int UnitId { get; set; }

		[Required]
		public int State { get; set; }

		public DateTime Timestamp { get; set; }

		// Transitioning away from the GeoLocationData blob beacuse its
		// too difficult to check and handle. We often need to split the values and cannot be ensured that 
		// the string data is well fromed. Too Magic stringie. 
		public string GeoLocationData { get; set; }

		public int? DestinationId { get; set; }

		public DateTime? LocalTimestamp { get; set; }

		public string Note { get; set; }

		// Not using DBGeography for now. 3 reasons: 
		//    1.) SQL CE Doesn't support it, 
		//    2.) Azure had issues with it and the SQLTypes DLL and 
		//    3.) Ties us too tightly to SQL Server
		//public DbGeography Location { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		[DecimalPrecision(6, 2)]
		public decimal? Accuracy { get; set; }

		[DecimalPrecision(7, 2)]
		public decimal? Altitude { get; set; }

		[DecimalPrecision(6, 2)]
		public decimal? AltitudeAccuracy { get; set; }

		[DecimalPrecision(5, 2)]
		public decimal? Speed { get; set; }

		[DecimalPrecision(5, 2)]
		public decimal? Heading { get; set; }

		[ForeignKey("UnitId")]
		public virtual Unit Unit { get; set; }

		public virtual ICollection<UnitStateRole> Roles { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitStateId; }
			set { UnitStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UnitStates";

		[NotMapped]
		public string IdName => "UnitStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Roles", "Unit" };

		public string GetStatusText()
		{
			if (State == (int)UnitStateTypes.Available)
				return "Available";
			if (State == (int)UnitStateTypes.Unavailable)
				return "Unavailable";
			if (State == (int)UnitStateTypes.OutOfService)
				return "Out of Service";
			if (State == (int)UnitStateTypes.Committed)
				return "Committed";
			if (State == (int)UnitStateTypes.Delayed)
				return "Delayed";
			if (State == (int)UnitStateTypes.Responding)
				return "Responding";
			if (State == (int)UnitStateTypes.OnScene)
				return "On Scene";
			if (State == (int)UnitStateTypes.Staging)
				return "Staging";
			if (State == (int)UnitStateTypes.Returning)
				return "Returning";
			if (State == (int)UnitStateTypes.Cancelled)
				return "Cancelled";
			if (State == (int)UnitStateTypes.Released)
				return "Released";
			if (State == (int)UnitStateTypes.Manual)
				return "Manual";
			if (State == (int)UnitStateTypes.Enroute)
				return "Enroute";

			return "Unknown";
		}

		public string GetStatusCss()
		{
			if (State == (int)UnitStateTypes.Available)
				return "";
			if (State == (int)UnitStateTypes.Unavailable)
				return "label-danger";
			if (State == (int)UnitStateTypes.OutOfService)
				return "label-danger";
			if (State == (int)UnitStateTypes.Committed)
				return "label-info";
			if (State == (int)UnitStateTypes.Delayed)
				return "label-warning";
			if (State == (int)UnitStateTypes.Responding)
				return "label-success";
			if (State == (int)UnitStateTypes.OnScene)
				return "label-onscene";
			if (State == (int)UnitStateTypes.Staging)
				return "label-primary";
			if (State == (int)UnitStateTypes.Returning)
				return "label-returning";
			if (State == (int)UnitStateTypes.Cancelled)
				return "label-default";
			if (State == (int)UnitStateTypes.Released)
				return "label-default";
			if (State == (int)UnitStateTypes.Manual)
				return "label-default";
			if (State == (int)UnitStateTypes.Enroute)
				return "label-enroute";

			return "";
		}

		public bool HasLocation()
		{
			if (Latitude.HasValue && Latitude.Value != 0 && Longitude.HasValue && Longitude.Value != 0)
				return true;

			return false;
		}
	}
}

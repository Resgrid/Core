using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using GeoCoordinatePortable;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	[Table("ResourceOrders")]
	public class ResourceOrder : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ResourceOrderId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int Type { get; set; }

		public bool AllowPartialFills { get; set; }

		public string Title { get; set; }

		public string IncidentNumber { get; set; }

		public string IncidentName { get; set; }

		public string IncidentAddress { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? IncidentLatitude { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? IncidentLongitude { get; set; }

		public string Summary { get; set; }

		public DateTime OpenDate { get; set; }

		public DateTime NeededBy { get; set; }

		public DateTime? MeetupDate { get; set; }

		public DateTime? CloseDate { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public string SpecialInstructions { get; set; }

		public string MeetupLocation { get; set; }

		public string FinancialCode { get; set; }

		public bool AutomaticFillAcceptance { get; set; }

		public int Visibility { get; set; }

		public int Range { get; set; }

		[DecimalPrecision(10, 7)]
		public double? OriginLatitude { get; set; }

		[DecimalPrecision(10, 7)]
		public double? OriginLongitude { get; set; }

		[NotMapped]
		public GeoCoordinate OriginLocation
		{
			get
			{
				if (OriginLatitude.HasValue && OriginLongitude.HasValue)
					return new GeoCoordinate(OriginLatitude.Value, OriginLongitude.Value);

				return new GeoCoordinate(0, 0);
			}
		}

		public double GetDistanceTo(double latitude, double longitude)
		{
			if (OriginLocation == null)
				return 0;

			return Math.Round(OriginLocation.GetDistanceTo(new GeoCoordinate(latitude, longitude)) / 1609.344, 2);
		}

		public virtual ICollection<ResourceOrderItem> Items { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ResourceOrderId; }
			set { ResourceOrderId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ResourceOrders";

		[NotMapped]
		public string IdName => "ResourceOrderId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "OriginLocation", "Items"};

		public bool IsFilled()
		{
			if (Items == null || !Items.Any())
				return true;

			return Items.All(x => x.IsFilled());
		}

		public decimal FilledPrecentage()
		{
			if (Items == null || !Items.Any())
				return 100;

			decimal totalUnits = Items.Sum(x => x.Min);
			decimal? totalFills = Items.Sum(x => x.Fills?.Count);

			if (totalFills == null || totalFills == 0)
				return 0;

			var result = (totalFills.Value / totalUnits) * 100;

			return result;
		}
	}

	public enum ResourceOrderTypes
	{
		OpenUntilClosed = 0,
		OpenUntilFilled = 1,
		OpenUntilDatePartial = 2,
		OpenUntilDateCancel = 3
	}
}

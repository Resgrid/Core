using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Pois")]
	public class Poi : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PoiId { get; set; }

		[Required]
		public int PoiTypeId { get; set; }

		[ForeignKey("PoiTypeId")]
		public virtual PoiType Type { get; set; }

		public double Longitude { get; set; }

		public double Latitude { get; set; }

		public string Name { get; set; }

		public string Address { get; set; }

		public string Note { get; set; }

		/// <summary>The background import (<see cref="PoiImport"/>) that created this POI; null for POIs added by hand.</summary>
		public string PoiImportId { get; set; }

		/// <summary>
		/// With <see cref="PoiImportId"/>: which of the import's located rows this POI is (0-based, file order). Unique per import,
		/// so a resumed import carries on after the last row it saved and can never save a row twice.
		/// </summary>
		public int? PoiImportRow { get; set; }

		/// <summary>
		/// The <see cref="PoiCapability"/> ids this POI offers, in the department's capability order. Filled by
		/// <c>IMappingService.PopulatePoiDetailsAsync</c>; not a column (see <see cref="PoiCapabilityAssignment"/>).
		/// </summary>
		[NotMapped]
		public List<string> CapabilityIds { get; set; }

		/// <summary>The names of <see cref="CapabilityIds"/>, same order. Filled with them.</summary>
		[NotMapped]
		public List<string> Capabilities { get; set; }

		/// <summary>The POI's current availability report, or null when it is open. Filled with <see cref="CapabilityIds"/>.</summary>
		[NotMapped]
		public PoiAvailability Availability { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PoiId; }
			set { PoiId = (int)value; }
		}


		[NotMapped]
		public string TableName => "Pois";

		[NotMapped]
		public string IdName => "PoiId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Type", "CapabilityIds", "Capabilities", "Availability" };
	}
}

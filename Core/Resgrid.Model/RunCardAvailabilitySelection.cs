using Newtonsoft.Json;
using ProtoBuf;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// Marks a unit status, personnel status, or staffing level as "dispatchable" for a
	/// run card. Cards with no selections of a given kind fall back to the
	/// AvailabilityMatrix Available classification for that kind.
	/// </summary>
	[Table("RunCardAvailabilitySelections")]
	[ProtoContract]
	public class RunCardAvailabilitySelection : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int RunCardAvailabilitySelectionId { get; set; }

		[Required]
		[ForeignKey("RunCard"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int RunCardId { get; set; }

		public virtual RunCard RunCard { get; set; }

		/// <summary>RunCardSelectionTypes value.</summary>
		[Required]
		[ProtoMember(3)]
		public int SelectionType { get; set; }

		/// <summary>
		/// Only for SelectionType = UnitStatus: scopes the selection to one unit type's
		/// custom status set. Null = applies to all unit types without a scoped row.
		/// </summary>
		[ProtoMember(4)]
		public int? UnitTypeId { get; set; }

		/// <summary>
		/// True when StateId is a CustomStateDetailId; false when it is a built-in
		/// UnitStateTypes / ActionTypes / UserStateTypes value.
		/// </summary>
		[ProtoMember(5)]
		public bool IsCustomState { get; set; }

		[Required]
		[ProtoMember(6)]
		public int StateId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardAvailabilitySelectionId; }
			set { RunCardAvailabilitySelectionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardAvailabilitySelections";

		[NotMapped]
		public string IdName => "RunCardAvailabilitySelectionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "RunCard" };
	}
}

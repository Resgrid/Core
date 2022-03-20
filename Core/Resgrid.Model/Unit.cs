using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("Units")]
	[ProtoContract]
	public class Unit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int UnitId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[Required]
		[ProtoMember(3)]
		public string Name { get; set; }

		[ProtoMember(4)]
		public string Type { get; set; }

		[ForeignKey("StationGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(5)]
		public int? StationGroupId { get; set; }

		[ProtoMember(6)]
		public string VIN { get; set; }

		[ProtoMember(7)]
		public string PlateNumber { get; set; }

		[ProtoMember(8)]
		public bool? FourWheel { get; set; }

		[ProtoMember(9)]
		public bool? SpecialPermit { get; set; }

		public virtual DepartmentGroup StationGroup { get; set; }

		public virtual Department Department { get; set; }

		[NotMapped]
		public virtual List<UnitRole> Roles { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitId; }
			set { UnitId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Units";

		[NotMapped]
		public string IdName => "UnitId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "StationGroup", "Department", "Roles" };
	}
}

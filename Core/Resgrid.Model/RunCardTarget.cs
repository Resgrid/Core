using Newtonsoft.Json;
using ProtoBuf;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// One resource a package row or a condition operand accepts ("any of these"): a unit type, a unit class, any unit,
	/// a personnel role or any person. A row or operand matches a resource when any of its targets does. Kept in one
	/// table so every reference to a department's unit types and roles can be validated and cleaned up in one place.
	/// </summary>
	[Table("RunCardTargets")]
	[ProtoContract]
	public class RunCardTarget : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int RunCardTargetId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		/// <summary>A <see cref="RunCardTargetOwners"/> value.</summary>
		[ProtoMember(3)]
		public int OwnerKind { get; set; }

		/// <summary>ResponsePackageRowId or RunCardDecisionNodeId, depending on <see cref="OwnerKind"/>.</summary>
		[ProtoMember(4)]
		public int OwnerId { get; set; }

		/// <summary>A <see cref="RunCardTargetKinds"/> value.</summary>
		[ProtoMember(5)]
		public int TargetKind { get; set; }

		/// <summary>UnitTypeId, UnitTypeClasses value or PersonnelRoleId; null for "any unit" and "any person".</summary>
		[ProtoMember(6)]
		public int? TargetRefId { get; set; }

		[NotMapped]
		public bool IsUnitTarget => TargetKind == (int)RunCardTargetKinds.UnitType || TargetKind == (int)RunCardTargetKinds.UnitClass || TargetKind == (int)RunCardTargetKinds.AnyUnit;

		[NotMapped]
		public bool IsPersonTarget => TargetKind == (int)RunCardTargetKinds.PersonnelRole || TargetKind == (int)RunCardTargetKinds.AnyPerson;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardTargetId; }
			set { RunCardTargetId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardTargets";

		[NotMapped]
		public string IdName => "RunCardTargetId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsUnitTarget", "IsPersonTarget" };

		public RunCardTarget Copy()
		{
			return new RunCardTarget
			{
				RunCardTargetId = RunCardTargetId,
				DepartmentId = DepartmentId,
				OwnerKind = OwnerKind,
				OwnerId = OwnerId,
				TargetKind = TargetKind,
				TargetRefId = TargetRefId
			};
		}
	}
}

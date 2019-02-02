using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallUnits")]
	[ProtoContract]
	public class CallUnit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallUnitId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ProtoMember(3)]
		[ForeignKey("Unit"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int UnitId { get; set; }

		[ProtoMember(4)]
		public virtual Unit Unit { get; set; }

		[ProtoMember(5)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		[ForeignKey("UnitState"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(6)]
		public int? UnitStateId { get; set; }

		public virtual UnitState UnitState { get; set; }

		[NotMapped]
		public object Id
		{
			get { return CallUnitId; }
			set { CallUnitId = (int)value; }
		}
	}
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CallDispatchUnits")]
	[ProtoContract]
	public class CallDispatchUnit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchUnitId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ForeignKey("Unit"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(3)]
		public int UnitId { get; set; }

		public virtual Unit Unit { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		public DateTime DispatchedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallDispatchUnitId; }
			set { CallDispatchUnitId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallDispatchUnits";

		[NotMapped]
		public string IdName => "CallDispatchUnitId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Unit", "Call" };
	}
}

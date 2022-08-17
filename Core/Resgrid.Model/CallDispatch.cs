using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CallDispatches")]
	[ProtoContract]
	public class CallDispatch: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallDispatchId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ProtoMember(3)]
		public string UserId { get; set; }

		[ProtoMember(4)]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(5)]
		public int? GroupId { get; set; }

		[ProtoMember(7)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		public DateTime DispatchedOn { get; set; }

		[ForeignKey("ActionLog"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(6)]
		public int? ActionLogId { get; set; }

		public virtual ActionLog ActionLog { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallDispatchId; }
			set { CallDispatchId = (int)value; }
		}

		
		[NotMapped]
		public string TableName => "CallDispatches";

		[NotMapped]
		public string IdName => "CallDispatchId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "ActionLog", "User", "Call" };
	}
}

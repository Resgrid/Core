using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("CallLogs")]
	public class CallLog : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CallLogId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(4000)]
		public string Narrative { get; set; }

		public int CallId { get; set; }

		[ForeignKey("CallId")]
		public virtual Call Call { get; set; }

		public DateTime LoggedOn { get; set; }

		public string LoggedByUserId { get; set; }

		public virtual Department Department { get; set; }

		public virtual IdentityUser LoggedBy { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallLogId; }
			set { CallLogId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallLogs";

		[NotMapped]
		public string IdName => "CallLogId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Call", "Department", "LoggedBy" };
	}
}

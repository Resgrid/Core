using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("CallTypes")]
	public class CallType : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CallTypeId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Type { get; set; }

		public int? MapIconType { get; set; }

		/// <summary>Key of the department's uploaded <see cref="TypeIcon"/>; null when none is uploaded (M0285).</summary>
		[MaxLength(64)]
		public string IconKey { get; set; }

		/// <summary>What the call type is, a <see cref="CallTypeClasses"/> value; -1 (None) until the department sets it.</summary>
		public int BaseType { get; set; } = (int)CallTypeClasses.None;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallTypeId; }
			set { CallTypeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallTypes";

		[NotMapped]
		public string IdName => "CallTypeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}

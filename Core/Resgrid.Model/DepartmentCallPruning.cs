using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentCallPruning")]
	public class DepartmentCallPruning: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentCallPruningId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		public bool? PruneUserEnteredCalls { get; set; }

		public int? UserCallPruneInterval { get; set; }

		public bool? PruneEmailImportedCalls { get; set; }

		public int? EmailImportCallPruneInterval { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentCallPruningId; }
			set { DepartmentCallPruningId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentCallPruning";

		[NotMapped]
		public string IdName => "DepartmentCallPruningId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}

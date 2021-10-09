using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UnitLogs")]
	public class UnitLog : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitLogId { get; set; }

		[Required]
		public int UnitId { get; set; }

		public DateTime Timestamp { get; set; }

		[Required]
		[MaxLength(4000)]
		public string Narrative { get; set; }

		[ForeignKey("UnitId")]
		public virtual Unit Unit { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitLogId; }
			set { UnitLogId = (int)value; }
		}


		[NotMapped]
		public string TableName => "UnitLogs";

		[NotMapped]
		public string IdName => "UnitLogId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Unit" };
	}
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class WorkshiftEntity : IEntity
	{
		[Required]
		public string WorkshiftEntityId { get; set; }

		[Required]
		public string WorkshiftId { get; set; }

		[JsonIgnore]
		public virtual Workshift Shift { get; set; }

		public string BackingId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WorkshiftEntityId; }
			set { WorkshiftEntityId = (string)value; }
		}

		[NotMapped]
		public string TableName => "WorkshiftEntities";

		[NotMapped]
		public string IdName => "WorkshiftEntityId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Shift" };
	}
}

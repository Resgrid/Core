using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class WorkshiftDay : IEntity
	{
		[Required]
		public string WorkshiftDayId { get; set; }

		[Required]
		public string WorkshiftId { get; set; }

		[JsonIgnore]
		public virtual Workshift Shift { get; set; }

		public DateTime Day { get; set; }

		public bool Processed { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WorkshiftDayId; }
			set { WorkshiftDayId = (string)value; }
		}

		[NotMapped]
		public string TableName => "WorkshiftDays";

		[NotMapped]
		public string IdName => "WorkshiftDayId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Shift" };
	}
}

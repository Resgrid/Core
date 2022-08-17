using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;


namespace Resgrid.Model
{
	public class WorkshiftFill : IEntity
	{
		[Required]
		public string WorkshiftFillId { get; set; }

		[Required]
		public string WorkshiftId { get; set; }

		[JsonIgnore]
		public virtual Workshift Shift { get; set; }

		[Required]
		public string WorkshiftDayId { get; set; }

		[JsonIgnore]
		public virtual WorkshiftDay Day { get; set; }

		[Required]
		public string WorkshiftEntityId { get; set; }

		[JsonIgnore]
		public virtual WorkshiftEntity Entity { get; set; }

		[Required]
		public string FilledById { get; set; }

		public string ReferenceId { get; set; }

		public bool Approved { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedById { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WorkshiftFillId; }
			set { WorkshiftFillId = (string)value; }
		}

		[NotMapped]
		public string TableName => "WorkshiftFills";

		[NotMapped]
		public string IdName => "WorkshiftFillId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Shift", "Day", "Entity" };
	}
}

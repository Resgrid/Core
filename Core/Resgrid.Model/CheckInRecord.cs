using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class CheckInRecord : IEntity
	{
		public string CheckInRecordId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public int CheckInType { get; set; }

		public string UserId { get; set; }

		public int? UnitId { get; set; }

		public string Latitude { get; set; }

		public string Longitude { get; set; }

		public DateTime Timestamp { get; set; }

		public string Note { get; set; }

		[NotMapped]
		public string TableName => "CheckInRecords";

		[NotMapped]
		public string IdName => "CheckInRecordId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CheckInRecordId; }
			set { CheckInRecordId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}

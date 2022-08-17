using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	public class Autofill : IEntity
	{
		public string AutofillId { get; set; }

		public int Type { get; set; }

		public int Sort { get; set; }

		public int DepartmentId { get; set; }

		public string AddedByUserId { get; set; }

		public string Name { get; set; }

		public string Data { get; set; }

		public DateTime AddedOn { get; set; }

		public object IdValue
		{
			get { return AutofillId; }
			set { AutofillId = value.ToString(); }
		}

		[NotMapped]
		public string TableName => "Autofills";

		[NotMapped]
		public string IdName => "AutofillId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}

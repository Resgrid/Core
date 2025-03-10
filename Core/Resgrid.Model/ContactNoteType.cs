using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	public class ContactNoteType : IEntity
	{
		[Required]
		public string ContactNoteTypeId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string Name { get; set; }

		public string Color { get; set; }

		public bool DefaultShouldAlert { get; set; }

		public int DefaultVisibility { get; set; } // 0 Internal, 1 Visible to Client

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime? EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		[NotMapped]
		public string TableName => "ContactNoteTypes";

		[NotMapped]
		public string IdName => "ContactNoteTypeId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactNoteTypeId; }
			set { ContactNoteTypeId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}

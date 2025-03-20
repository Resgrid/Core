using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	public class ContactNote : IEntity
	{
		[Required]
		public string ContactNoteId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string ContactId { get; set; }

		public string ContactNoteTypeId { get; set; }

		[Required]
		public string Note { get; set; }

		public bool ShouldAlert { get; set; }

		public int Visibility { get; set; } // 0 Internal, 1 Visible to Client

		public DateTime? ExpiresOn { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime? EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		[NotMapped]
		public ContactNoteType NoteType { get; set; }

		[NotMapped]
		public string TableName => "ContactNotes";

		[NotMapped]
		public string IdName => "ContactNoteId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactNoteId; }
			set { ContactNoteId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "NoteType" };
	}
}

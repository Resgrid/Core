using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	public class ContactAssociation : IEntity
	{
		[Required]
		public string ContactAssociationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string SourceContactId { get; set; }

		[Required]
		public string TargetContactId { get; set; }

		public int Type { get; set; } // 0 Related

		public string Note { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		[NotMapped]
		public string TableName => "ContactAssociations";

		[NotMapped]
		public string IdName => "ContactAssociationId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactAssociationId; }
			set { ContactAssociationId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}

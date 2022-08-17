using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	public class Contact : IEntity
	{
		[Required]
		public string ContactId { get; set; }

		[Required]
		public string ContactTypeId { get; set; }

		[Required]
		[MaxLength(500)]
		public string Name { get; set; }



		[NotMapped]
		public string TableName => "Contacts";

		[NotMapped]
		public string IdName => "ContactId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactId; }
			set { ContactId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}

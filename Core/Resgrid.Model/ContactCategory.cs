using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	public class ContactCategory : IEntity
	{
		public string ContactCategoryId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string Name { get; set; }

		public string Description { get; set; }

		public string Color { get; set; }

		public bool DisplayOnMap { get; set; }

		public int MapIcon { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime? EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		[NotMapped]
		public IEnumerable<Contact> Contacts { get; set; }

		[NotMapped]
		public string TableName => "ContactCategories";

		[NotMapped]
		public string IdName => "ContactCategoryId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactCategoryId; }
			set { ContactCategoryId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Contacts" };

		public ContactCategory()
		{
			Contacts = new List<Contact>();
		}
	}
}

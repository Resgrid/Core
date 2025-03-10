using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	public class Contact : IEntity
	{
		public string ContactId { get; set; }

		public int DepartmentId { get; set; }

		public int ContactType { get; set; } // 0 = Person, 1 = Company

		public string OtherName { get; set; }

		public string ContactCategoryId { get; set; }

		public virtual ContactCategory Category { get; set; }

		public string FirstName { get; set; }

		public string MiddleName { get; set; }

		public string LastName { get; set; }

		public string CompanyName { get; set; }

		public string Email { get; set; }

		public int? PhysicalAddressId { get; set; }

		public int? MailingAddressId { get; set; }

		public string Website { get; set; }

		public string Twitter { get; set; }

		public string Facebook { get; set; }

		public string LinkedIn { get; set; }

		public string Instagram { get; set; }

		public string Threads { get; set; }

		public string Bluesky { get; set; }

		public string Mastodon { get; set; }

		public string LocationGpsCoordinates { get; set; }

		public string EntranceGpsCoordinates { get; set; }

		public string ExitGpsCoordinates { get; set; }

		public string LocationGeofence { get; set; }

		public string CountryIssuedIdNumber { get; set; }

		public string CountryIdName { get; set; }

		public string StateIdNumber { get; set; }

		public string StateIdName { get; set; }

		public string StateIdCountryName { get; set; }

		public string Description { get; set; }

		public string OtherInfo { get; set; }

		public string HomePhoneNumber { get; set; }

		public string CellPhoneNumber { get; set; }

		public string FaxPhoneNumber { get; set; }

		public string OfficePhoneNumber { get; set; }

		public byte[] Image { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime? EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		public string GetName()
		{
			if (ContactType == 0)
			{
				return $"{FirstName} {LastName}";
			}
			else
			{
				return CompanyName;
			}
		}

		public string GetTypeName()
		{
			if (ContactType == 0)
			{
				return "Person";
			}
			else
			{
				return "Location\\Company";
			}
		}

		public string GetCategoryName()
		{
			if (Category != null)
				return Category.Name;

			return "None";
		}

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
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Category" };
	}
}

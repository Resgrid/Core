using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;
using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.CallTypes
{
	/// <summary>
	/// Gets the contact
	/// </summary>
	public class ContactResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public ContactResultData Data { get; set; }
	}

	/// <summary>
	/// A contact
	/// </summary>
	public class ContactResultData
	{
		/// <summary>
		/// ADP: true when this row belongs to a protection-enforced department (shield indicator).
		/// Protected values here are broker-decrypted plaintext or the exact "REDACTED" placeholder
		/// — never ciphertext.
		/// </summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP: machine-readable reason when values are redacted (step_up_required,
		/// grant_expired, grant_revoked, protected_access_denied, broker_unavailable); null when
		/// nothing is redacted.</summary>
		public string ProtectedReason { get; set; }

		/// <summary>ADP: stable catalog field ids ("contacts.email") whose values are REDACTED.</summary>
		public List<string> RedactedFields { get; set; } = new List<string>();

		public string ContactId { get; set; }

		public int ContactType { get; set; } // 0 = Person, 1 = Company

		public string OtherName { get; set; }

		public string ContactCategoryId { get; set; }

		public virtual ContactCategory Category { get; set; }

		/// <summary>The contact category's display name and color (the Category entity itself is never sent).</summary>
		public string CategoryName { get; set; }

		public string CategoryColor { get; set; }

		/// <summary>Resolved physical address (contact detail only; null on list rows or when none is set).</summary>
		public ContactAddressData PhysicalAddress { get; set; }

		/// <summary>Resolved mailing address when it differs from the physical one (contact detail only).</summary>
		public ContactAddressData MailingAddress { get; set; }

		/// <summary>Mobile-visible custom field values with their labels, in form order (contact detail only).</summary>
		public List<ContactCustomFieldData> CustomFields { get; set; } = new List<ContactCustomFieldData>();

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

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime AddedOnUtc { get; set; }

		public string AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public string AddedByUserName { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? EditedOnUtc { get; set; }

		public string EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		public string EditedByUserName { get; set; }

		/// <summary>
		/// User Defined Field values for this contact
		/// </summary>
		public List<UdfFieldValueResultData> UdfValues { get; set; }
	}

	public class ContactAddressData
	{
		public string Address1 { get; set; }
		public string City { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		/// <summary>One-line form for display and for handing to a maps app.</summary>
		public string Formatted { get; set; }
	}

	public class ContactCustomFieldData
	{
		public string UdfFieldId { get; set; }
		public string Label { get; set; }
		public string Value { get; set; }
		/// <summary>What to show for Value: option keys resolved to labels, booleans as Yes/No, sealed values as REDACTED.</summary>
		public string DisplayValue { get; set; }
		public int FieldDataType { get; set; }
		public string GroupName { get; set; }
		public int SortOrder { get; set; }
	}
}

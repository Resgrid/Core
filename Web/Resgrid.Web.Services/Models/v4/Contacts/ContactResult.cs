using Resgrid.Model;
using System;
using System.Collections.Generic;

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
		public string ContactId { get; set; }

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

		public DateTime AddedOnUtc { get; set; }

		public string AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public string AddedByUserName { get; set; }

		public DateTime? EditedOnUtc { get; set; }

		public string EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		public string EditedByUserName { get; set; }
	}
}

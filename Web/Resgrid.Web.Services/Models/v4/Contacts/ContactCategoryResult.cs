using System;

namespace Resgrid.Web.Services.Models.v4.CallTypes
{
	/// <summary>
	/// Gets the contact category
	/// </summary>
	public class ContactCategoryResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public ContactResultData Data { get; set; }
	}

	/// <summary>
	/// A contact category
	/// </summary>
	public class ContactCategoryResultData
	{
		/// <summary>
		/// Unique identifier for the contact category
		/// </summary>
		public string ContactCategoryId { get; set; }

		/// <summary>
		/// Display name of the contact category
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Optional description of the contact category
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Color code used for visual identification of this category
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// Date and time when this category was created in UTC
		/// </summary>
		public DateTime AddedOnUtc { get; set; }

		/// <summary>
		/// Date and time when this category was created in local department time zone
		/// </summary>
		public string AddedOn { get; set; }

		/// <summary>
		/// User identifier of who created this category
		/// </summary>
		public string AddedByUserId { get; set; }

		/// <summary>
		/// Name of who created this category
		/// </summary>
		public string AddedByUserName { get; set; }

		/// <summary>
		/// Date and time when this category was last modified, null if never edited in UTC
		/// </summary>
		public DateTime? EditedOnUtc { get; set; }

		/// <summary>
		/// Date and time when this category was last modified, null if never edited  in local department time zone
		/// </summary>
		public string EditedOn { get; set; }

		/// <summary>
		/// User identifier of who last modified this category
		/// </summary>
		public string EditedByUserId { get; set; }

		/// <summary>
		/// Name of who last edited the Category
		/// </summary>
		public string EditedByUserName { get; set; }

	}
}

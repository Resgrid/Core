using Resgrid.Web.Services.Models.v4.CallTypes;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Contacts
{
	/// <summary>
	/// Gets the contact categories
	/// </summary>
	public class ContactsCategoriesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ContactCategoryResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ContactsCategoriesResult()
		{
			Data = new List<ContactCategoryResultData>();
		}
	}
}

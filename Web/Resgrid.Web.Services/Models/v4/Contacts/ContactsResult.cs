using Resgrid.Web.Services.Models.v4.CallTypes;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Contacts
{
	/// <summary>
	/// Gets the contact
	/// </summary>
	public class ContactsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ContactResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ContactsResult()
		{
			Data = new List<ContactResultData>();
		}
	}
}

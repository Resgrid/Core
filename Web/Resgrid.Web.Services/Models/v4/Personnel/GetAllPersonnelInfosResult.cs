using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Personnel
{
	/// <summary>
	/// Result containing all the data required to populate the New Call form
	/// </summary>
	public class GetAllPersonnelInfosResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<PersonnelInfoResultData> Data { get; set; }
	}
}

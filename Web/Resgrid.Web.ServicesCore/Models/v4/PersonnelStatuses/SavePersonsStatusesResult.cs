using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.PersonnelStatuses
{
	/// <summary>
	/// Depicts a result after saving a person status
	/// </summary>
	public class SavePersonsStatusesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<string> Ids { get; set; }
	}
}

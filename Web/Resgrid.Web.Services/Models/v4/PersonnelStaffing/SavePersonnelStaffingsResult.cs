using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.PersonnelStaffing
{
	/// <summary>
	/// Depicts a result after saving a person staffing
	/// </summary>
	public class SavePersonnelStaffingsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<string> Ids { get; set; }
	}
}

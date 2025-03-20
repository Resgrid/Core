using System;

namespace Resgrid.Web.Services.Models.v4.PersonnelStatuses
{
	/// <summary>
	/// Depicts a result after saving a person status
	/// </summary>
	public class SavePersonStatusResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public string Id { get; set; }
	}
}

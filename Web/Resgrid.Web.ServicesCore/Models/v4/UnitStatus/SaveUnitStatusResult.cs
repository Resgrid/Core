using System;

namespace Resgrid.Web.Services.Models.v4.UnitStatus
{
	/// <summary>
	/// Depicts a result after saving a unit status
	/// </summary>
	public class SaveUnitStatusResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public string Id { get; set; }
	}
}

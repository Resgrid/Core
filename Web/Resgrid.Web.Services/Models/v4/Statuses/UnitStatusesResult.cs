using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Statuses
{
	/// <summary>
	/// Unit statuses result
	/// </summary>
	public class UnitStatusesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<UnitTypeStatusResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public UnitStatusesResult()
		{
			Data = new List<UnitTypeStatusResultData>();
		}
	}

	/// <summary>
	/// A status set for a unit type
	/// </summary>
	public class UnitTypeStatusResultData
	{
		/// <summary>
		/// Unit types for these statuses
		/// </summary>
		public string UnitType { get; set; }

		/// <summary>
		/// Unit types for these statuses
		/// </summary>
		public string StatusId { get; set; }

		/// <summary>
		/// Statuses
		/// </summary>
		public List<StatusResultData> Statuses { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public UnitTypeStatusResultData()
		{
			Statuses = new List<StatusResultData>();
		}
	}
}

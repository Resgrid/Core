using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.Services.Models.v4.CustomStatuses;
using Resgrid.Web.Services.Models.v4.Groups;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Dispatch
{
	/// <summary>
	/// Data needed for the Dispatch App Modal that sets the state for a unit
	/// </summary>
	public class GetSetUnitStateResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetSetUnitStateResultData Data { get; set; }
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class GetSetUnitStateResultData
	{
		/// <summary>
		/// Unit id
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// Name of the unit
		/// </summary>
		public string UnitName { get; set; }

		/// <summary>
		/// Stations the Unit can respond to
		/// </summary>
		public List<GroupResultData> Stations { get; set; }

		/// <summary>
		/// Calls the unit can respond to
		/// </summary>
		public List<CallResultData> Calls { get; set; }

		/// <summary>
		/// Status types
		/// </summary>
		public List<CustomStatusResultData> Statuses { get; set; }
	}
}

using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.RunCards
{
	/// <summary>
	/// All run cards for the department.
	/// </summary>
	public class RunCardsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<RunCardData> Data { get; set; } = new List<RunCardData>();
	}

	/// <summary>
	/// A single run card.
	/// </summary>
	public class RunCardResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public RunCardData Data { get; set; }
	}

	/// <summary>
	/// Identifier of the saved run card.
	/// </summary>
	public class SaveRunCardResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Run card identifier
		/// </summary>
		public string Id { get; set; }
	}

	/// <summary>
	/// Preview of what the department's run cards would dispatch for a prospective call.
	/// </summary>
	public class RunCardRecommendationResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public DispatchRecommendationResult Data { get; set; }
	}
}

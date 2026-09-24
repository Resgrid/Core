using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Dispatch
{
	/// <summary>
	/// The nearest available unit board for an incident location: every unit (a team, an apparatus or an
	/// individual set up as a unit) and every responder in the caller's dispatch scope, with status, live
	/// position, ETA, shift coverage and role mix.
	/// </summary>
	public class GetNearestUnitsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public NearestUnitBoard Data { get; set; }
	}
}

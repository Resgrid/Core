using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Road closures (M0314): drawn closed or slowed roads and areas with their details and time windows, and the closures in
	/// effect for a routed trip.
	/// </summary>
	public interface IRoadClosureService
	{
		/// <summary>Every closure of the department (enabled or not, past, current and future), newest first.</summary>
		Task<List<RoadClosure>> GetClosuresForDepartmentAsync(int departmentId, bool bypassCache = false);

		/// <summary>The closure when it belongs to the department; null otherwise.</summary>
		Task<RoadClosure> GetClosureByIdAsync(int departmentId, string roadClosureId);

		/// <summary>The department's closures in effect at <paramref name="utc"/> (any travel mode).</summary>
		Task<List<RoadClosure>> GetActiveClosuresAsync(int departmentId, DateTime utc);

		/// <summary>
		/// Validates and saves (inserts when <see cref="RoadClosure.RoadClosureId"/> is empty). Throws
		/// <see cref="RoadClosureValidationException"/> when the closure is not valid; nothing is saved then.
		/// </summary>
		Task<RoadClosure> SaveClosureAsync(RoadClosure closure, string userId, CancellationToken cancellationToken = default);

		/// <summary>Switches a closure on or off; false when it isn't the department's.</summary>
		Task<bool> SetClosureEnabledAsync(int departmentId, string roadClosureId, bool enabled, string userId, CancellationToken cancellationToken = default);

		/// <summary>Deletes a closure; false when it isn't the department's.</summary>
		Task<bool> DeleteClosureAsync(int departmentId, string roadClosureId, CancellationToken cancellationToken = default);

		/// <summary>The closures a trip by <paramref name="mode"/> must respect at <paramref name="utc"/>, ready for routing.</summary>
		Task<List<RoutingClosure>> GetRoutingClosuresAsync(int departmentId, UnitTravelModes mode, DateTime utc);
	}

	/// <summary>Why a road closure could not be saved.</summary>
	public enum RoadClosureValidationErrors
	{
		NameRequired = 0,
		NameTooLong = 1,
		ShapeRequired = 2,
		ShapeInvalid = 3,
		ShapeTooLarge = 4,
		TravelModeRequired = 5,
		TooManyWindows = 6,
		WindowEndsBeforeStart = 7,
		TextTooLong = 8,
		InvalidType = 9
	}

	public class RoadClosureValidationException : Exception
	{
		public RoadClosureValidationException(RoadClosureValidationErrors error, string field, string message)
			: base(message)
		{
			Error = error;
			Field = field;
		}

		public RoadClosureValidationErrors Error { get; }

		/// <summary>The closure property at fault, for the form to mark.</summary>
		public string Field { get; }
	}
}

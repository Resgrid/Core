using System;

namespace Resgrid.Model
{
	/// <summary>
	/// A unit can run one route at a time: starting another while one is in progress or paused is refused with the running instance's id,
	/// so a client can offer to end it first. Derives from <see cref="InvalidOperationException"/>, which callers already catch.
	/// </summary>
	public class RouteInProgressException : InvalidOperationException
	{
		public RouteInProgressException(string routeInstanceId, bool paused)
			: base(paused ? "Unit already has a paused route instance." : "Unit already has an active route instance.")
		{
			RouteInstanceId = routeInstanceId;
			Paused = paused;
		}

		/// <summary>The unit's running (or paused) instance.</summary>
		public string RouteInstanceId { get; }

		public bool Paused { get; }
	}
}

using System.Collections.Generic;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.StaticMaps.Entities
{
	/// <summary>
	/// The markers parameter defines a set of one or more markers at a set of locations. Each marker defined within a single markers declaration must exhibit the same visual style; if you wish to display markers with different styles, you will need to supply multiple markers parameters with separate style information.
	/// </summary>
	public class Marker
	{
		/// <summary>
		/// Marker's style
		/// </summary>
		public MarkerStyle Style { get; set; }

		public IList<ILocationString> Locations { get; set; }
	}
}
using System.Collections.Generic;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.StaticMaps.Entities
{
	public class Path
	{
		public PathStyle Style { get; set; }

		public IList<ILocationString> Locations { get; set; }
	}
}
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.BigBoardX;
using Resgrid.Web.Areas.User.Models.Home;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class MapDataJson
	{
		public ICollection<MapMakerInfo> Markers { get; set; }
		public ICollection<GeofenceJson> Geofences { get; set; }
		public ICollection<MapMakerInfo> Pois { get; set; }

		//public ICollection<PersonnelViewModel> Personnel { get; set; }
		//public ICollection<UnitViewModel> Units { get; set; }
		//public ICollection<CallViewModel> Calls { get; set; }
		//public BigBoardMapModel MapModel { get; set; }
		//public ICollection<GroupViewModel> Groups { get; set; }

		public MapDataJson()
		{
			Markers = new List<MapMakerInfo>();
			Geofences = new List<GeofenceJson>();
			Pois = new List<MapMakerInfo>();
		}
	}
}
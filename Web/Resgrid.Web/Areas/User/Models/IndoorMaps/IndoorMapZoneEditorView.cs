using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.IndoorMaps
{
	public class IndoorMapZoneEditorView : BaseUserModel
	{
		public IndoorMapFloor Floor { get; set; }
		public IndoorMap IndoorMap { get; set; }
		public List<IndoorMapZone> Zones { get; set; }
	}
}

using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CustomMaps
{
	public class CustomMapRegionEditorView : BaseUserModel
	{
		public IndoorMapFloor Layer { get; set; }
		public IndoorMap Map { get; set; }
		public List<IndoorMapZone> Regions { get; set; }
	}
}

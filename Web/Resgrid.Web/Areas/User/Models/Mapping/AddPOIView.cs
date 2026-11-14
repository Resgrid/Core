using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class AddPOIView
	{
		public int TypeId { get; set; }
		public string Message { get; set; }
		public Poi Poi { get; set; }

		/// <summary>The department's POI capabilities, offered as checkboxes (none: a link to the capabilities page instead).</summary>
		public List<PoiCapability> Capabilities { get; set; } = new List<PoiCapability>();

		/// <summary>The capabilities this POI offers: posted back as the ticked boxes.</summary>
		public List<string> SelectedCapabilityIds { get; set; } = new List<string>();
	}
}
using Resgrid.Model;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class ManageFloorsView
	{
		public string CustomMapId { get; set; }
		public string CustomMapName { get; set; }
		public string Message { get; set; }

		public List<CustomMapFloor> Floors { get; set; } = new List<CustomMapFloor>();

		// New floor form fields
		[Display(Name = "Floor Name")]
		public string NewFloorName { get; set; }

		[Display(Name = "Floor Number")]
		public int NewFloorNumber { get; set; }

		[Display(Name = "Sort Order")]
		public int NewSortOrder { get; set; }

		[Display(Name = "Is Default Floor")]
		public bool NewIsDefault { get; set; }

		[Display(Name = "Elevation (meters)")]
		public double? NewElevation { get; set; }

		[Display(Name = "Floor Image")]
		public IFormFile FloorImage { get; set; }
	}
}


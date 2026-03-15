using Resgrid.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class EditCustomMapView
	{
		public string CustomMapId { get; set; }
		public string Message { get; set; }

		[Required]
		[Display(Name = "Map Name")]
		public string Name { get; set; }

		[Display(Name = "Description")]
		public string Description { get; set; }

		[Required]
		[Display(Name = "Map Type")]
		public int Type { get; set; }

		[Display(Name = "Is Active")]
		public bool IsActive { get; set; }

		[Display(Name = "Default Zoom")]
		[Range(1, 22)]
		public int DefaultZoom { get; set; }

		[Display(Name = "Min Zoom")]
		[Range(1, 22)]
		public int MinZoom { get; set; }

		[Display(Name = "Max Zoom")]
		[Range(1, 22)]
		public int MaxZoom { get; set; }

		[Display(Name = "Bounds Top-Left Latitude")]
		public double BoundsTopLeftLat { get; set; }

		[Display(Name = "Bounds Top-Left Longitude")]
		public double BoundsTopLeftLng { get; set; }

		[Display(Name = "Bounds Bottom-Right Latitude")]
		public double BoundsBottomRightLat { get; set; }

		[Display(Name = "Bounds Bottom-Right Longitude")]
		public double BoundsBottomRightLng { get; set; }

		[Display(Name = "Event Starts On")]
		public DateTime? EventStartsOn { get; set; }

		[Display(Name = "Event Ends On")]
		public DateTime? EventEndsOn { get; set; }

		public Coordinates CenterCoordinates { get; set; }

		public List<SelectListItem> MapTypes { get; set; } = new List<SelectListItem>();
	}
}


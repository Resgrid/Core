using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Resgrid.Web.Services.Controllers.Version3.Models.BigBoard
{
	public class MapMakerInfo
	{
		public string Id { get; set; }
		public double Longitude { get; set; }
		public double Latitude { get; set; }
		public string Title { get; set; }
		public int zIndex { get; set; }
		public string ImagePath { get; set; }
		public string InfoWindowContent { get; set; }
		public string Color { get; set; }
	}
}

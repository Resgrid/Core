using Resgrid.Model;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class UploadFloorImageView
	{
		public string CustomMapFloorId { get; set; }
		public string CustomMapId { get; set; }
		public string CustomMapName { get; set; }
		public string FloorName { get; set; }
		public string Message { get; set; }

		// Current image state
		public string CurrentImageUrl { get; set; }
		public CustomMapFloorStorageType CurrentStorageType { get; set; }
		public int? ImageWidthPx { get; set; }
		public int? ImageHeightPx { get; set; }
		public int? TileZoomLevels { get; set; }

		/// <summary>File size threshold (in MB) at which tiled pyramid storage is used.</summary>
		public long ThresholdMb { get; set; } = 10;
	}
}


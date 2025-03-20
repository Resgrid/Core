using Resgrid.Model;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Mapping;

public class EditLayerView
{
	public string Message { get; set; }

	[Required]
	public string Id { get; set; }

	[Required]
	public string Name { get; set; }
	public bool IsSearchable { get; set; }
	public bool IsOnByDefault { get; set; }

	[Required]
	public string Color { get; set; }

	[Required]
	public string GeoJson { get; set; }
	public Department Department { get; set; }
	public Coordinates CenterCoordinates { get; set; }
}

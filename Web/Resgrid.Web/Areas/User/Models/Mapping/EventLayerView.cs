using System;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping;

/// <summary>
/// Mapping → Layers → Import Layer and a layer's Details page: an imported (KML, KMZ, GeoJSON) or drawn layer with the
/// event it maps (dates in the department's time zone, organizer and contact).
/// </summary>
public class EventLayerView
{
	/// <summary>The layer, blank on the import page.</summary>
	public string Id { get; set; }

	[Required]
	[MaxLength(250)]
	public string Name { get; set; }

	[MaxLength(2000)]
	public string Description { get; set; }

	/// <summary>The color for shapes the file gives no color of their own.</summary>
	[Required]
	[MaxLength(50)]
	public string Color { get; set; } = "#e8590c";

	public bool IsOnByDefault { get; set; }

	public bool IsSearchable { get; set; } = true;

	/// <summary>Event start, in the department's time zone.</summary>
	public DateTime? StartsOn { get; set; }

	/// <summary>Event end, in the department's time zone.</summary>
	public DateTime? EndsOn { get; set; }

	[MaxLength(250)]
	public string Organizer { get; set; }

	[MaxLength(250)]
	public string ContactName { get; set; }

	[MaxLength(100)]
	public string ContactPhone { get; set; }

	[MaxLength(250)]
	public string ContactEmail { get; set; }

	// Shown on the Details page only.
	public string SourceFileName { get; set; }

	public DateTime? ImportedOn { get; set; }

	public int ShapeCount { get; set; }

	public bool IsImported { get; set; }

	public MapLayerEventPhases Phase { get; set; }

	public string TimeZoneName { get; set; }
}

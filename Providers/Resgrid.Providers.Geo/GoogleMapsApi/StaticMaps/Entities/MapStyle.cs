using GoogleMapsApi.StaticMaps.Enums;

namespace GoogleMapsApi.StaticMaps.Entities
{
	/// <summary>
	/// Styled maps allow you to customize the presentation of the standard Google map styles, changing the visual display of such elements as roads, parks, and built-up areas to reflect a different style than that used in the default map type. These components are known as features and a styled map allows you to select these features and apply visual styles to their display (including hiding them entirely). With these changes, the map can be made to emphasize particular components or complement content within the surrounding page.
	/// A customized "styled" map consists of one or more specified styles, each indicated through a style parameter within the Static Map request URL. Additional styles are specified by passing additional style parameters. A style consists of a selection(s) and a set of rules to apply to that selection. The rules indicate what visual modification to make to the selection.
	/// </summary>
	public class MapStyle
	{
		/// <summary>
		/// (optional) indicates what features to select for this style modification. (See Map Features below.) If no feature argument is passed, all features will be selected.
		/// </summary>
		public MapFeature MapFeature { get; set; }

		/// <summary>
		/// (optional) indicates what sub-set of the selected features to select. (See Map Elements below.) If no element argument is passed, all elements of the given feature will be selected.
		/// </summary>
		public MapElement MapElement { get; set; }

		/// <summary>
		/// (an RGB hex string of format 0xRRGGBB) indicates the basic color to apply to the selection. 
		/// </summary>
		public string HUE { get; set; }

		/// <summary>
		/// (a floating point value between -100 and 100) indicates the percentage change in brightness of the element. Negative values increase darkness (where -100 specifies black) while positive values increase brightness (where +100 specifies white).
		/// </summary>
		public float? Lightness { get; set; }

		/// <summary>
		///  (a floating point value between -100 and 100) indicates the percentage change in intensity of the basic color to apply to the element.
		/// </summary>
		public float? Saturation { get; set; }

		/// <summary>
		/// (a floating point value between 0.01 and 10.0, where 1.0 applies no correction) indicates the amount of gamma correction to apply to the element. Gammas modify the lightness of hues in a non-linear fashion, while unaffecting white or black values. Gammas are typically used to modify the contrast of multiple elements. For example, you could modify the gamma to increase or decrease the contrast between the edges and interiors of elements. Low gamma values (less than 1) increase contrast, while high values (> 1) decrease contrast.
		/// </summary>
		public float? Gamma { get; set; }

		/// <summary>
		///  simply inverts the existing lightness.
		/// </summary>
		public bool InverseLightness { get; set; }

		/// <summary>
		/// indicates whether and how the element appears on the map. visibility:simplified indicates that the map should simplify the presentation of those elements as it sees fit. (A simplified road structure may show fewer roads, for example.)
		/// </summary>
		public MapVisibility MapVisibility { get; set; }
	}
}
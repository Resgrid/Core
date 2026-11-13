using GeoJSON.Net.Feature;

namespace Resgrid.Model
{
	/// <summary>Why an uploaded map layer file could not be used.</summary>
	public enum MapLayerFileReadErrors
	{
		None = 0,
		/// <summary>Not a KML, KMZ or GeoJSON file.</summary>
		UnsupportedType = 1,
		/// <summary>The file could not be parsed (malformed XML/JSON, damaged KMZ).</summary>
		Unreadable = 2,
		/// <summary>Parsed, but no point, line or polygon with usable coordinates.</summary>
		NoShapes = 3,
		/// <summary>More shapes than a layer can hold (<see cref="MapLayerFileLimits.MaxFeatures"/>).</summary>
		TooManyShapes = 4,
		/// <summary>More coordinates than a layer can hold (<see cref="MapLayerFileLimits.MaxPositions"/>).</summary>
		TooManyPoints = 5,
		/// <summary>The file, or the KML inside a KMZ, is larger than <see cref="MapLayerFileLimits.MaxFileBytes"/>.</summary>
		TooLarge = 6
	}

	/// <summary>Limits on one imported layer. A layer is one document, and every map that shows it loads all of it.</summary>
	public static class MapLayerFileLimits
	{
		public const long MaxFileBytes = 30L * 1024 * 1024;
		public const int MaxFeatures = 20000;
		public const int MaxPositions = 250000;

		/// <summary>
		/// Most bytes a layer's shapes may take as one stored document. MongoDB refuses a document over 16 MB, and descriptions
		/// repeated over thousands of shapes can stay inside the shape and point limits and still not fit.
		/// </summary>
		public const long MaxStoredBytes = 15L * 1024 * 1024;
	}

	/// <summary>
	/// The shapes read from an uploaded KML, KMZ or GeoJSON file: plain points, lines and polygons (multi-geometries are
	/// split), each with "name", "description", "color" and "category" properties where the file had them.
	/// </summary>
	public class MapLayerFileReadResult
	{
		public FeatureCollection Features { get; set; } = new FeatureCollection();

		public MapLayerFileReadErrors Error { get; set; }

		public int Points { get; set; }

		public int Lines { get; set; }

		public int Polygons { get; set; }

		/// <summary>Placemarks or features with no coordinates (for example a Google My Maps place located only by address).</summary>
		public int SkippedWithoutGeometry { get; set; }

		/// <summary>Shapes dropped for coordinates outside the world or too few points to draw.</summary>
		public int SkippedInvalid { get; set; }

		public bool Success => Error == MapLayerFileReadErrors.None;

		public int ShapeCount => Points + Lines + Polygons;
	}
}

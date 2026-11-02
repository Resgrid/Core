using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Web Mercator XYZ tiles for georeferenced custom map layers (maplibre-mapping-routing-navigation-plan.md §6.7). The
	/// pixel-pyramid tiles the existing GetCustomMapTile route serves are unchanged.
	/// </summary>
	public interface ICustomMapMercatorTileRenderer
	{
		/// <summary>The layer's corner bounds in normalized Mercator space, or null for a pixel-only layer.</summary>
		Task<MercatorLayerBounds> GetBoundsAsync(IndoorMapFloor layer);

		/// <summary>A 256 px PNG tile, or null when the tile does not touch the layer (or the layer has no bounds).</summary>
		Task<byte[]> RenderTileAsync(IndoorMapFloor layer, int z, int x, int y);
	}

	/// <summary>Layer bounds in normalized Web Mercator coordinates (0..1, Y down from the north edge).</summary>
	public class MercatorLayerBounds
	{
		public double North { get; set; }

		public double South { get; set; }

		public double West { get; set; }

		public double East { get; set; }
	}
}

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>A GIF search hit returned to chat clients; urls point at the GIF CDN, never at Resgrid.</summary>
	public class GifSearchResult
	{
		public string Id { get; set; }
		public string Title { get; set; }
		/// <summary>Small preview/thumbnail url for the picker grid.</summary>
		public string PreviewUrl { get; set; }
		/// <summary>Full GIF url embedded in the message metadata.</summary>
		public string GifUrl { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
	}

	/// <summary>
	/// Server-side GIF search proxy (Giphy or Tenor per ChatConfig.GifProvider) so provider API keys
	/// never ship to clients.
	/// </summary>
	public interface IGifProvider
	{
		/// <summary>True when a provider + API key are configured.</summary>
		bool IsConfigured { get; }

		Task<List<GifSearchResult>> SearchAsync(string query, int limit, int offset);

		/// <summary>Trending/featured GIFs for an empty search box.</summary>
		Task<List<GifSearchResult>> TrendingAsync(int limit);
	}
}

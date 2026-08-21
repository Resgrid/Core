using System.Collections.Generic;
using System.Threading.Tasks;
using ProtoBuf;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// A GIF search hit returned to chat clients; urls point at the GIF CDN, never at Resgrid.
	/// Search results are cached, and the cache provider serializes with protobuf-net, so this type needs a
	/// contract — without one every cache write throws and the per-query cache silently never populates.
	/// </summary>
	[ProtoContract]
	public class GifSearchResult
	{
		[ProtoMember(1)]
		public string Id { get; set; }

		[ProtoMember(2)]
		public string Title { get; set; }

		/// <summary>Small preview/thumbnail url for the picker grid.</summary>
		[ProtoMember(3)]
		public string PreviewUrl { get; set; }

		/// <summary>Full GIF url embedded in the message metadata.</summary>
		[ProtoMember(4)]
		public string GifUrl { get; set; }

		[ProtoMember(5)]
		public int Width { get; set; }

		[ProtoMember(6)]
		public int Height { get; set; }
	}

	/// <summary>
	/// Server-side GIF search proxy (Giphy, per ChatConfig.GifProvider) so provider API keys never
	/// ship to clients. Results are capped to a workplace-safe content rating (ChatConfig.GifRating).
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

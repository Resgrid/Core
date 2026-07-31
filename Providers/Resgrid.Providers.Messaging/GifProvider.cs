using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Messaging
{
	/// <summary>
	/// GIF search proxy for chat. Talks to Giphy or Tenor (per ChatConfig.GifProvider) server-side so
	/// the API key never reaches clients. Failures return empty result sets — GIF search is never a
	/// hard dependency.
	/// </summary>
	public class GifProvider : IGifProvider
	{
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(10)
		};

		public bool IsConfigured
		{
			get
			{
				if (string.Equals(ChatConfig.GifProvider, "giphy", StringComparison.OrdinalIgnoreCase))
					return !string.IsNullOrWhiteSpace(ChatConfig.GiphyApiKey);

				if (string.Equals(ChatConfig.GifProvider, "tenor", StringComparison.OrdinalIgnoreCase))
					return !string.IsNullOrWhiteSpace(ChatConfig.TenorApiKey);

				return false;
			}
		}

		public async Task<List<GifSearchResult>> SearchAsync(string query, int limit, int offset)
		{
			if (!IsConfigured || string.IsNullOrWhiteSpace(query))
				return new List<GifSearchResult>();

			try
			{
				if (string.Equals(ChatConfig.GifProvider, "tenor", StringComparison.OrdinalIgnoreCase))
					return await TenorRequestAsync($"https://tenor.googleapis.com/v2/search?q={Uri.EscapeDataString(query)}&key={ChatConfig.TenorApiKey}&limit={Clamp(limit)}&pos={offset}");

				return await GiphyRequestAsync($"https://api.giphy.com/v1/gifs/search?api_key={ChatConfig.GiphyApiKey}&q={Uri.EscapeDataString(query)}&limit={Clamp(limit)}&offset={offset}&rating=pg-13");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new List<GifSearchResult>();
			}
		}

		public async Task<List<GifSearchResult>> TrendingAsync(int limit)
		{
			if (!IsConfigured)
				return new List<GifSearchResult>();

			try
			{
				if (string.Equals(ChatConfig.GifProvider, "tenor", StringComparison.OrdinalIgnoreCase))
					return await TenorRequestAsync($"https://tenor.googleapis.com/v2/featured?key={ChatConfig.TenorApiKey}&limit={Clamp(limit)}");

				return await GiphyRequestAsync($"https://api.giphy.com/v1/gifs/trending?api_key={ChatConfig.GiphyApiKey}&limit={Clamp(limit)}&rating=pg-13");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new List<GifSearchResult>();
			}
		}

		private static async Task<List<GifSearchResult>> GiphyRequestAsync(string url)
		{
			var json = await _httpClient.GetStringAsync(url);
			var payload = JObject.Parse(json);

			return (payload["data"] as JArray ?? new JArray())
				.Select(item => new GifSearchResult
				{
					Id = (string)item["id"],
					Title = (string)item["title"],
					PreviewUrl = (string)item.SelectToken("images.fixed_width_small.url"),
					GifUrl = (string)item.SelectToken("images.fixed_width.url"),
					Width = ParseInt(item.SelectToken("images.fixed_width.width")),
					Height = ParseInt(item.SelectToken("images.fixed_width.height"))
				})
				.Where(r => !string.IsNullOrWhiteSpace(r.GifUrl))
				.ToList();
		}

		private static async Task<List<GifSearchResult>> TenorRequestAsync(string url)
		{
			var json = await _httpClient.GetStringAsync(url);
			var payload = JObject.Parse(json);

			return (payload["results"] as JArray ?? new JArray())
				.Select(item =>
				{
					var gif = item.SelectToken("media_formats.gif") ?? item.SelectToken("media_formats.tinygif");
					var preview = item.SelectToken("media_formats.tinygif") ?? gif;
					var dims = gif?["dims"] as JArray;

					return new GifSearchResult
					{
						Id = (string)item["id"],
						Title = (string)item["title"] ?? (string)item["content_description"],
						PreviewUrl = (string)preview?["url"],
						GifUrl = (string)gif?["url"],
						Width = dims != null && dims.Count > 0 ? ParseInt(dims[0]) : 0,
						Height = dims != null && dims.Count > 1 ? ParseInt(dims[1]) : 0
					};
				})
				.Where(r => !string.IsNullOrWhiteSpace(r.GifUrl))
				.ToList();
		}

		private static int Clamp(int limit)
		{
			if (limit <= 0)
				return 25;

			return Math.Min(limit, 50);
		}

		private static int ParseInt(object value)
		{
			return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
		}
	}
}

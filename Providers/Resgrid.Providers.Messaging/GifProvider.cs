using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Messaging
{
	/// <summary>
	/// GIF search proxy for chat. Talks to Giphy server-side so the API key never reaches clients.
	/// Results are capped to a workplace-safe content rating (ChatConfig.GifRating, "g" by default).
	/// Failures return empty result sets — GIF search is never a hard dependency.
	/// </summary>
	public class GifProvider : IGifProvider
	{
		private static readonly HttpClient _httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(10)
		};

		private static readonly TimeSpan SearchCacheDuration = TimeSpan.FromSeconds(60);
		private const int MaxOffset = 5000;

		// Giphy content ratings this proxy will ever request; "r" is deliberately not allowed.
		private static readonly string[] AllowedRatings = { "g", "pg", "pg-13" };

		private readonly ICacheProvider _cacheProvider;

		public GifProvider(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public bool IsConfigured
		{
			get
			{
				if (string.Equals(ChatConfig.GifProvider, "giphy", StringComparison.OrdinalIgnoreCase))
					return !string.IsNullOrWhiteSpace(ChatConfig.GiphyApiKey);

				return false;
			}
		}

		public async Task<List<GifSearchResult>> SearchAsync(string query, int limit, int offset)
		{
			if (!IsConfigured || string.IsNullOrWhiteSpace(query))
				return new List<GifSearchResult>();

			// Short per-query cache: identical searches are common (picker re-open, scroll re-fetch)
			// and each uncached call burns provider API quota.
			var cacheKey = $"gifsearch:giphy:{Rating()}:{query.Trim().ToLowerInvariant()}:{Clamp(limit)}:{ClampOffset(offset)}";

			async Task<List<GifSearchResult>> search()
			{
				try
				{
					return await GiphyRequestAsync($"https://api.giphy.com/v1/gifs/search?api_key={ChatConfig.GiphyApiKey}&q={Uri.EscapeDataString(query)}&limit={Clamp(limit)}&offset={ClampOffset(offset)}&rating={Rating()}");
				}
				catch (Exception ex)
				{
					LogSanitizedException(ex);
					return new List<GifSearchResult>();
				}
			}

			if (_cacheProvider != null)
				return await _cacheProvider.RetrieveAsync(cacheKey, search, SearchCacheDuration) ?? new List<GifSearchResult>();

			return await search();
		}

		public async Task<List<GifSearchResult>> TrendingAsync(int limit)
		{
			if (!IsConfigured)
				return new List<GifSearchResult>();

			try
			{
				return await GiphyRequestAsync($"https://api.giphy.com/v1/gifs/trending?api_key={ChatConfig.GiphyApiKey}&limit={Clamp(limit)}&rating={Rating()}");
			}
			catch (Exception ex)
			{
				LogSanitizedException(ex);
				return new List<GifSearchResult>();
			}
		}

		/// <summary>Sanitized content rating: only g/pg/pg-13 ever reach the provider; default "g".</summary>
		private static string Rating()
		{
			var rating = ChatConfig.GifRating?.Trim().ToLowerInvariant();
			return AllowedRatings.Contains(rating) ? rating : "g";
		}

		// Belt-and-suspenders scrub for key=/api_key= query params. A bounded match timeout caps regex work
		// on pathological input (ReDoS guard); on timeout the literal-key replacements have already removed
		// the real secrets, so falling through without the query-param scrub is safe.
		private static readonly Regex KeyQueryParamRegex = new Regex(
			@"\b(api_key|key)=[^&\s""']+",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(250));

		// GetStringAsync failures can carry the request URL (with key=/api_key=) in the exception
		// message; scrub configured keys and any key query params before emitting to logs.
		private static void LogSanitizedException(Exception ex)
		{
			var text = ex?.ToString() ?? string.Empty;

			if (!string.IsNullOrWhiteSpace(ChatConfig.GiphyApiKey))
				text = text.Replace(ChatConfig.GiphyApiKey, "***");

			try
			{
				text = KeyQueryParamRegex.Replace(text, "$1=***");
			}
			catch (RegexMatchTimeoutException)
			{
				// Query-param scrub timed out; literal key replacements above already removed the secrets.
			}

			Logging.LogError(text);
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
				.Where(r => !string.IsNullOrWhiteSpace(r.GifUrl) && IsAllowedCdnUrl(r.GifUrl) && IsAllowedCdnUrl(r.PreviewUrl))
				.ToList();
		}

		private static bool IsAllowedCdnUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return true;

			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
				return false;

			var allowedHosts = ChatConfig.GifCdnHosts;
			if (allowedHosts == null || allowedHosts.Length == 0)
				return true;

			return allowedHosts.Any(host => !string.IsNullOrWhiteSpace(host)
				&& (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
					|| uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)));
		}

		private static int ClampOffset(int offset)
		{
			if (offset <= 0)
				return 0;

			return Math.Min(offset, MaxOffset);
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

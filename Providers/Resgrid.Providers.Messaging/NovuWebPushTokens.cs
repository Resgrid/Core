using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Providers.Messaging
{
	/// <summary>
	/// The list arithmetic and Novu response reading behind web push tokens. A web channel is written with a
	/// replacing PUT (Novu's only way to drop a token), so the whole list is rebuilt here from what the
	/// subscriber already holds: every signed-in browser survives a new registration, newest last.
	/// </summary>
	public static class NovuWebPushTokens
	{
		/// <summary>
		/// Puts the token at the end of the list (moving it there when it was already present, so a browser
		/// that keeps registering is never the one evicted) and drops the oldest past <paramref name="max"/>.
		/// </summary>
		public static List<string> Add(IEnumerable<string>? existing, string token, int max)
		{
			var tokens = Clean(existing).Where(x => x != token).ToList();
			tokens.Add(token);

			if (max < 1)
				max = 1;

			if (tokens.Count > max)
				tokens.RemoveRange(0, tokens.Count - max);

			return tokens;
		}

		public static List<string> Remove(IEnumerable<string>? existing, string token)
		{
			return Clean(existing).Where(x => x != token).ToList();
		}

		/// <summary>
		/// The ids of every integration carrying this identifier. A subscriber channel names its integration
		/// by id only, never by identifier. An identifier is unique within an environment, but an older
		/// Novu lists every environment's integrations to an API key. Matching against the whole set still
		/// finds the right channel, because a subscriber lives in exactly one environment.
		/// </summary>
		/// <returns>The matching ids; empty when none match; null when the body could not be read.</returns>
		public static HashSet<string>? FindIntegrationIds(string? integrationsJson, string identifier)
		{
			var root = Parse(integrationsJson);
			if (root == null)
				return null;

			var items = root.Type == JTokenType.Array ? root : root.SelectToken("data");
			if (items == null || items.Type != JTokenType.Array)
				return null;

			var ids = new HashSet<string>();
			foreach (var item in items.Children().OfType<JObject>())
			{
				var id = item.Value<string>("_id");
				if (!string.IsNullOrWhiteSpace(id) && string.Equals(item.Value<string>("identifier"), identifier, StringComparison.Ordinal))
					ids.Add(id);
			}

			return ids;
		}

		/// <summary>
		/// The device tokens on the subscriber's channel for one of <paramref name="integrationIds"/>.
		/// </summary>
		/// <returns>The tokens; empty when the subscriber has no such channel; null when the body could not be read.</returns>
		public static List<string>? FindChannelTokens(string? subscriberJson, ISet<string> integrationIds)
		{
			var root = Parse(subscriberJson);
			if (root == null || root.Type != JTokenType.Object)
				return null;

			var subscriber = root.SelectToken("data") is JObject data ? data : (JObject)root;
			var channels = subscriber.SelectToken("channels");

			if (channels == null || channels.Type == JTokenType.Null)
				return new List<string>();

			if (channels.Type != JTokenType.Array)
				return null;

			foreach (var channel in channels.Children().OfType<JObject>())
			{
				var integrationId = channel.Value<string>("_integrationId");
				if (integrationId == null || !integrationIds.Contains(integrationId))
					continue;

				var deviceTokens = channel.SelectToken("credentials.deviceTokens");
				if (deviceTokens == null || deviceTokens.Type != JTokenType.Array)
					return new List<string>();

				return Clean(deviceTokens.Children().Where(x => x.Type == JTokenType.String).Select(x => x.ToString())).ToList();
			}

			return new List<string>();
		}

		private static IEnumerable<string> Clean(IEnumerable<string>? tokens)
		{
			return (tokens ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
		}

		private static JToken? Parse(string? json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				return JToken.Parse(json);
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}
}

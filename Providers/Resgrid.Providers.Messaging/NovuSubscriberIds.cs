using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Providers.Messaging
{
	public enum NovuSubscriberKind
	{
		User,
		ICUser,
		Unit
	}

	/// <summary>A subscriber id NovuProvider creates, taken apart.</summary>
	public sealed record NovuSubscriber(string SubscriberId, string Code, NovuSubscriberKind Kind, string Id);

	/// <summary>
	/// Reading the subscriber ids NovuProvider creates — {code}_User_{userId}, {code}_IC_User_{userId} and
	/// {code}_Unit_{unitId} — and Novu's subscriber list. Used to find the subscribers of departments that no longer
	/// exist (deleted before department deletion started removing them).
	/// </summary>
	public static class NovuSubscriberIds
	{
		private const string ICUserMarker = "IC_User_";
		private const string UserMarker = "User_";
		private const string UnitMarker = "Unit_";

		/// <summary>
		/// The parts of a subscriber id, or null when it is not one NovuProvider creates. A department code is letters
		/// and digits only, so the first underscore ends it.
		/// </summary>
		public static NovuSubscriber? Parse(string? subscriberId)
		{
			if (string.IsNullOrWhiteSpace(subscriberId))
				return null;

			var separator = subscriberId.IndexOf('_');
			if (separator <= 0)
				return null;

			var code = subscriberId.Substring(0, separator);
			var rest = subscriberId.Substring(separator + 1);

			if (rest.StartsWith(ICUserMarker, StringComparison.Ordinal))
				return Build(subscriberId, code, NovuSubscriberKind.ICUser, rest.Substring(ICUserMarker.Length));

			if (rest.StartsWith(UserMarker, StringComparison.Ordinal))
				return Build(subscriberId, code, NovuSubscriberKind.User, rest.Substring(UserMarker.Length));

			if (rest.StartsWith(UnitMarker, StringComparison.Ordinal))
			{
				var unitId = rest.Substring(UnitMarker.Length);
				return int.TryParse(unitId, out var parsed) && parsed > 0 && parsed.ToString() == unitId
					? Build(subscriberId, code, NovuSubscriberKind.Unit, unitId)
					: null;
			}

			return null;
		}

		/// <summary>
		/// The subscribers whose department code belongs to no existing department. Ids NovuProvider did not create
		/// are never returned.
		/// </summary>
		public static List<NovuSubscriber> FindOrphans(IEnumerable<string>? subscriberIds, ISet<string> existingCodes)
		{
			return (subscriberIds ?? Enumerable.Empty<string>())
				.Select(Parse)
				.Where(s => s != null && !existingCodes.Contains(s.Code))
				.Select(s => s!)
				.ToList();
		}

		/// <summary>
		/// The user and IC user subscribers of members who are no longer in the department: the department still
		/// exists, but the user is not one of its current members. Only codes present in
		/// <paramref name="currentMembersByCode"/> are considered, so a department whose members could not be read is
		/// never judged; unit subscribers and departments that no longer exist are left to <see cref="FindOrphans"/>.
		/// </summary>
		public static List<NovuSubscriber> FindRemovedMembers(IEnumerable<string>? subscriberIds, IReadOnlyDictionary<string, ISet<string>> currentMembersByCode)
		{
			return (subscriberIds ?? Enumerable.Empty<string>())
				.Select(Parse)
				.Where(s => s != null && s.Kind != NovuSubscriberKind.Unit
					&& currentMembersByCode.TryGetValue(s.Code, out var members) && !members.Contains(s.Id))
				.Select(s => s!)
				.ToList();
		}

		/// <summary>
		/// The unit subscribers of units that are no longer in use: the department still exists, but the unit is not one
		/// of its current (non-deleted) units. Only codes present in <paramref name="currentUnitsByCode"/> are considered,
		/// so a department whose units could not be read is never judged.
		/// </summary>
		public static List<NovuSubscriber> FindDeletedUnits(IEnumerable<string>? subscriberIds, IReadOnlyDictionary<string, ISet<int>> currentUnitsByCode)
		{
			return (subscriberIds ?? Enumerable.Empty<string>())
				.Select(Parse)
				.Where(s => s != null && s.Kind == NovuSubscriberKind.Unit
					&& currentUnitsByCode.TryGetValue(s.Code, out var units) && !units.Contains(int.Parse(s.Id)))
				.Select(s => s!)
				.ToList();
		}

		/// <summary>
		/// The subscriber ids on one page of GET /v1/subscribers, and whether another page follows. Without a hasMore
		/// field, a full page is taken to mean there may be more.
		/// </summary>
		/// <returns>Ids null when the body could not be read.</returns>
		public static (List<string>? Ids, bool HasMore) ReadPage(string? json, int limit)
		{
			if (string.IsNullOrWhiteSpace(json))
				return (null, false);

			JToken root;
			try
			{
				root = JToken.Parse(json);
			}
			catch (JsonException)
			{
				return (null, false);
			}

			var items = root.Type == JTokenType.Array ? root : root.SelectToken("data");
			if (items == null || items.Type != JTokenType.Array)
				return (null, false);

			var ids = items.Children().OfType<JObject>()
				.Select(item => item.Value<string>("subscriberId"))
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Select(id => id!)
				.ToList();

			var hasMoreToken = root.Type == JTokenType.Object ? root.SelectToken("hasMore") : null;
			var hasMore = hasMoreToken != null && hasMoreToken.Type == JTokenType.Boolean
				? hasMoreToken.Value<bool>()
				: items.Children().Count() >= limit;

			return (ids, hasMore);
		}

		private static NovuSubscriber? Build(string subscriberId, string code, NovuSubscriberKind kind, string id)
		{
			return string.IsNullOrWhiteSpace(id) ? null : new NovuSubscriber(subscriberId, code, kind, id);
		}
	}
}

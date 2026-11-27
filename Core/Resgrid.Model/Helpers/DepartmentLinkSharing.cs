using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// Single source of truth for what a department may read through a <see cref="DepartmentLink"/>. Share flags are
	/// per direction: DepartmentShare* is what the creating department (<see cref="DepartmentLink.DepartmentId"/>) shares
	/// with the linked department, LinkedDepartmentShare* is what the linked department shares back. A viewer only ever
	/// reads the OTHER side's data, and only the kinds that side shares toward the viewer over an enabled link.
	/// </summary>
	public static class DepartmentLinkSharing
	{
		/// <summary>
		/// The department whose <paramref name="shareType"/> data <paramref name="viewerDepartmentId"/> may read through
		/// <paramref name="link"/>, or null when the link is missing or disabled, the viewer is not a party to it, or the
		/// other side does not share that kind of data toward the viewer.
		/// </summary>
		public static int? GetSharedDepartmentId(DepartmentLink link, int viewerDepartmentId, DepartmentLinkShareTypes shareType)
		{
			if (link == null || !link.LinkEnabled || viewerDepartmentId <= 0 || link.DepartmentId == link.LinkedDepartmentId)
				return null;

			if (link.LinkedDepartmentId == viewerDepartmentId)
				return DepartmentShares(link, shareType) ? link.DepartmentId : (int?)null;

			if (link.DepartmentId == viewerDepartmentId)
				return LinkedDepartmentShares(link, shareType) ? link.LinkedDepartmentId : (int?)null;

			return null;
		}

		/// <summary>True when the viewer is either party to the link (says nothing about what it may read).</summary>
		public static bool IsParty(DepartmentLink link, int viewerDepartmentId) =>
			link != null && viewerDepartmentId > 0 && (link.DepartmentId == viewerDepartmentId || link.LinkedDepartmentId == viewerDepartmentId);

		/// <summary>The other party to the link from the viewer's side, or null when the viewer is not a party.</summary>
		public static int? GetOtherDepartmentId(DepartmentLink link, int viewerDepartmentId)
		{
			if (!IsParty(link, viewerDepartmentId) || link.DepartmentId == link.LinkedDepartmentId)
				return null;

			return link.DepartmentId == viewerDepartmentId ? link.LinkedDepartmentId : link.DepartmentId;
		}

		private const string SubscriptionItemsKey = "DepartmentLinkShareGroups";

		/// <summary>
		/// Remembers, in a hub connection's Items, the share groups it joined for a link, so unsubscribing leaves exactly
		/// those groups even after the epoch rotated or the link was deleted.
		/// </summary>
		public static void RecordSubscription(IDictionary<object, object> connectionItems, int linkId, IEnumerable<string> groups)
		{
			if (connectionItems == null || groups == null)
				return;

			if (!(connectionItems.TryGetValue(SubscriptionItemsKey, out var value) && value is Dictionary<int, List<string>> subscriptions))
			{
				subscriptions = new Dictionary<int, List<string>>();
				connectionItems[SubscriptionItemsKey] = subscriptions;
			}

			if (!subscriptions.TryGetValue(linkId, out var joined))
				subscriptions[linkId] = joined = new List<string>();

			foreach (var group in groups)
			{
				if (!string.IsNullOrWhiteSpace(group) && !joined.Contains(group))
					joined.Add(group);
			}
		}

		/// <summary>
		/// Forgets the connection's subscription to a link and returns the groups to leave: those it joined for that link
		/// and does not still need for another link subscription (two links between the same departments share groups).
		/// Empty when the connection never subscribed to the link.
		/// </summary>
		public static List<string> TakeSubscription(IDictionary<object, object> connectionItems, int linkId)
		{
			if (connectionItems == null || !connectionItems.TryGetValue(SubscriptionItemsKey, out var value) ||
				!(value is Dictionary<int, List<string>> subscriptions) || !subscriptions.TryGetValue(linkId, out var joined))
				return new List<string>();

			subscriptions.Remove(linkId);
			var stillNeeded = new HashSet<string>(subscriptions.Values.SelectMany(groups => groups));

			return joined.Where(group => !stillNeeded.Contains(group)).ToList();
		}

		/// <summary>
		/// SignalR group a linked viewer joins to receive one kind of event for <paramref name="departmentId"/>. Publishers
		/// send the matching events here in addition to the department's own group, so a link subscription never sees the
		/// department's other traffic.
		/// <para>
		/// <paramref name="accessVersion"/> is the department's current link-share access epoch
		/// (<c>IDepartmentLinksService.GetShareAccessVersionAsync</c>). Disabling, deleting or narrowing a link rotates it, and
		/// publishers always send to the current epoch's group, so a connection that subscribed before the revocation is left
		/// in a group that never receives another event; it must subscribe again, which re-checks the link. Null when there
		/// is no epoch (cache unavailable): callers then neither join nor publish, rather than fall back to a fixed name a
		/// revoked connection could still be in.
		/// </para>
		/// </summary>
		public static string GetShareGroupName(int departmentId, DepartmentLinkShareTypes shareType, string accessVersion) =>
			string.IsNullOrWhiteSpace(accessVersion) ? null : $"DepartmentLinkShare:{departmentId}:{shareType}:{accessVersion}";

		/// <summary>
		/// True when <paramref name="updated"/> gives either party less than <paramref name="stored"/> did: the link was enabled
		/// and is now disabled, its parties changed, or a share flag was turned off. Revoking access means rotating both
		/// stored parties' link-share access epochs so existing realtime subscriptions stop receiving events.
		/// </summary>
		/// <summary>
		/// The canonical text a link acceptance signs (round 10 A3, e-Sign §20, L2): the link, its two parties and what each side shares at the
		/// moment it is accepted. A later sharing change does not touch the signature; it binds to what was accepted.
		/// </summary>
		public static string AcceptanceCanonical(DepartmentLink link) =>
			link == null ? string.Empty : string.Join("\n", "department-link-acceptance/1",
				link.DepartmentLinkId.ToString(System.Globalization.CultureInfo.InvariantCulture), link.DepartmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
				link.LinkedDepartmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
				"creator:" + Flags(link.DepartmentShareCalls, link.DepartmentShareUnits, link.DepartmentSharePersonnel, link.DepartmentShareOrders, link.DepartmentShareProtocols, link.DepartmentShareTraining),
				"linked:" + Flags(link.LinkedDepartmentShareCalls, link.LinkedDepartmentShareUnits, link.LinkedDepartmentSharePersonnel, link.LinkedDepartmentShareOrders,
					link.LinkedDepartmentShareProtocols, link.LinkedDepartmentShareTraining));

		private static string Flags(params bool[] values) => string.Concat(values.Select(v => v ? '1' : '0'));

		public static bool ReducesAccess(DepartmentLink stored, DepartmentLink updated)
		{
			if (stored == null || !stored.LinkEnabled)
				return false;

			if (updated == null || !updated.LinkEnabled || updated.DepartmentId != stored.DepartmentId || updated.LinkedDepartmentId != stored.LinkedDepartmentId)
				return true;

			return (stored.DepartmentShareCalls && !updated.DepartmentShareCalls) ||
				(stored.DepartmentShareUnits && !updated.DepartmentShareUnits) ||
				(stored.DepartmentSharePersonnel && !updated.DepartmentSharePersonnel) ||
				(stored.DepartmentShareOrders && !updated.DepartmentShareOrders) ||
				(stored.DepartmentShareProtocols && !updated.DepartmentShareProtocols) ||
				(stored.DepartmentShareTraining && !updated.DepartmentShareTraining) ||
				(stored.LinkedDepartmentShareCalls && !updated.LinkedDepartmentShareCalls) ||
				(stored.LinkedDepartmentShareUnits && !updated.LinkedDepartmentShareUnits) ||
				(stored.LinkedDepartmentSharePersonnel && !updated.LinkedDepartmentSharePersonnel) ||
				(stored.LinkedDepartmentShareOrders && !updated.LinkedDepartmentShareOrders) ||
				(stored.LinkedDepartmentShareProtocols && !updated.LinkedDepartmentShareProtocols) ||
				(stored.LinkedDepartmentShareTraining && !updated.LinkedDepartmentShareTraining);
		}

		/// <summary>
		/// True when <paramref name="link"/> is enabled and <paramref name="sharingDepartmentId"/> is one party and shares
		/// <paramref name="shareType"/> toward the other party, <paramref name="receivingDepartmentId"/>. The publisher/subscriber features
		/// (protocol and course sets, LMS sponsorship, countersignature) ask this on every read and write, so a revoked link or a withdrawn
		/// share kind cuts access at once.
		/// </summary>
		public static bool Shares(DepartmentLink link, int sharingDepartmentId, int receivingDepartmentId, DepartmentLinkShareTypes shareType) =>
			sharingDepartmentId > 0 && receivingDepartmentId > 0 && sharingDepartmentId != receivingDepartmentId &&
			GetSharedDepartmentId(link, receivingDepartmentId, shareType) == sharingDepartmentId;

		/// <summary>The enabled link between the two departments (either direction) over which <paramref name="sharingDepartmentId"/> shares
		/// <paramref name="shareType"/> toward <paramref name="receivingDepartmentId"/>, or null.</summary>
		public static DepartmentLink FindSharingLink(IEnumerable<DepartmentLink> links, int sharingDepartmentId, int receivingDepartmentId, DepartmentLinkShareTypes shareType) =>
			links?.Where(l => Shares(l, sharingDepartmentId, receivingDepartmentId, shareType)).OrderBy(l => l.DepartmentLinkId).FirstOrDefault();

		/// <summary>What <paramref name="link"/>'s <paramref name="departmentId"/> side shares toward the other side, by kind.</summary>
		public static bool SideShares(DepartmentLink link, int departmentId, DepartmentLinkShareTypes shareType)
		{
			if (link == null)
				return false;
			if (link.DepartmentId == departmentId)
				return DepartmentShares(link, shareType);
			if (link.LinkedDepartmentId == departmentId)
				return LinkedDepartmentShares(link, shareType);
			return false;
		}

		/// <summary>
		/// Sets what <paramref name="departmentId"/>'s side of <paramref name="link"/> shares (only that side's flags: a department never
		/// changes what the other side shares with it). False when the department is not a party.
		/// </summary>
		public static bool SetSideShares(DepartmentLink link, int departmentId, bool calls, bool units, bool personnel, bool orders, bool protocols, bool training)
		{
			if (!IsParty(link, departmentId) || link.DepartmentId == link.LinkedDepartmentId)
				return false;

			if (link.DepartmentId == departmentId)
			{
				link.DepartmentShareCalls = calls;
				link.DepartmentShareUnits = units;
				link.DepartmentSharePersonnel = personnel;
				link.DepartmentShareOrders = orders;
				link.DepartmentShareProtocols = protocols;
				link.DepartmentShareTraining = training;
			}
			else
			{
				link.LinkedDepartmentShareCalls = calls;
				link.LinkedDepartmentShareUnits = units;
				link.LinkedDepartmentSharePersonnel = personnel;
				link.LinkedDepartmentShareOrders = orders;
				link.LinkedDepartmentShareProtocols = protocols;
				link.LinkedDepartmentShareTraining = training;
			}

			return true;
		}

		/// <summary>The kinds of data that are pushed to link subscribers over the eventing hub.</summary>
		public static readonly DepartmentLinkShareTypes[] EventedShareTypes =
		{
			DepartmentLinkShareTypes.Calls,
			DepartmentLinkShareTypes.Units,
			DepartmentLinkShareTypes.Personnel
		};

		private static bool DepartmentShares(DepartmentLink link, DepartmentLinkShareTypes shareType)
		{
			switch (shareType)
			{
				case DepartmentLinkShareTypes.Calls: return link.DepartmentShareCalls;
				case DepartmentLinkShareTypes.Units: return link.DepartmentShareUnits;
				case DepartmentLinkShareTypes.Personnel: return link.DepartmentSharePersonnel;
				case DepartmentLinkShareTypes.Orders: return link.DepartmentShareOrders;
				case DepartmentLinkShareTypes.Protocols: return link.DepartmentShareProtocols;
				case DepartmentLinkShareTypes.Training: return link.DepartmentShareTraining;
				default: return false;
			}
		}

		private static bool LinkedDepartmentShares(DepartmentLink link, DepartmentLinkShareTypes shareType)
		{
			switch (shareType)
			{
				case DepartmentLinkShareTypes.Calls: return link.LinkedDepartmentShareCalls;
				case DepartmentLinkShareTypes.Units: return link.LinkedDepartmentShareUnits;
				case DepartmentLinkShareTypes.Personnel: return link.LinkedDepartmentSharePersonnel;
				case DepartmentLinkShareTypes.Orders: return link.LinkedDepartmentShareOrders;
				case DepartmentLinkShareTypes.Protocols: return link.LinkedDepartmentShareProtocols;
				case DepartmentLinkShareTypes.Training: return link.LinkedDepartmentShareTraining;
				default: return false;
			}
		}
	}
}

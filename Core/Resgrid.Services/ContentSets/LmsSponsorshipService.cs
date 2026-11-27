using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.ContentSets
{
	/// <summary>
	/// Sponsored LMS seats (T5 item 2a, R-L3; §25.4a items 8 and 8b; round 10 A3). See <see cref="ILmsSponsorshipService"/>.
	/// <para>
	/// A sponsorship pays only while three things hold, checked on every read: it is active, an enabled Department Link still shares Training
	/// from the sponsor toward the department, and the sponsor's own LMS add-on is entitled (the paid-plan rule applies to the payer). The
	/// sponsored department runs only its managed copies of the sponsor's course sets (<see cref="GetSponsoredCourseIdsAsync"/>); when a
	/// sponsorship stops paying, those copies keep the add-on's lapse rules from the time it stopped (when it ended, or when the sponsor's add-on
	/// lapsed), never longer.
	/// </para>
	/// </summary>
	public class LmsSponsorshipService : ILmsSponsorshipService
	{
		/// <summary>How long the copies of an ended sponsorship stay listed, so lapse rules (due dates, the 30-day undated window) can run out.</summary>
		public static readonly TimeSpan LapseHistory = TimeSpan.FromDays(366);

		private readonly ILmsSponsorshipsRepository _sponsorships;
		private readonly ILmsMemberSeatsRepository _seats;
		private readonly ILmsExternalLearnersRepository _externalLearners;
		private readonly ILmsAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentLinksService _links;
		private readonly IContentSetCopiesRepository _copies;
		private readonly IContentSetNotifier _notifier;
		private readonly IEventAggregator _events;

		public LmsSponsorshipService(ILmsSponsorshipsRepository sponsorships, ILmsMemberSeatsRepository seats, ILmsExternalLearnersRepository externalLearners,
			ILmsAccessService access, IDepartmentsService departments, IDepartmentLinksService links, IContentSetCopiesRepository copies, IContentSetNotifier notifier,
			IEventAggregator events)
		{
			_sponsorships = sponsorships;
			_seats = seats;
			_externalLearners = externalLearners;
			_access = access;
			_departments = departments;
			_links = links;
			_copies = copies;
			_notifier = notifier;
			_events = events;
		}

		private static int PackSize => Math.Max(1, LmsAddonConfig.PackSize);

		private async Task<string> NameAsync(int departmentId)
		{
			try
			{
				return (await _departments.GetDepartmentByIdAsync(departmentId, false))?.Name;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private async Task<int> CountedSeatsAsync(int departmentId)
		{
			var rows = (await _seats.GetForDepartmentAsync(departmentId) ?? new List<LmsMemberSeat>()).Where(r => r != null && r.DepartmentId == departmentId).ToList();
			if (rows.Count == 0)
				return 0;
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return LmsSeatRules.CountedSeats(rows, new HashSet<string>(active, StringComparer.OrdinalIgnoreCase)).Count;
		}

		/// <summary>An enabled link over which the sponsor shares Training toward the department now (the sponsorship's own link first).</summary>
		private async Task<DepartmentLink> SharingLinkAsync(int sponsorDepartmentId, int departmentId, int? preferredLinkId)
		{
			if (preferredLinkId.HasValue && preferredLinkId.Value > 0)
			{
				var preferred = await _links.GetLinkByIdAsync(preferredLinkId.Value);
				if (DepartmentLinkSharing.Shares(preferred, sponsorDepartmentId, departmentId, DepartmentLinkShareTypes.Training))
					return preferred;
			}

			return DepartmentLinkSharing.FindSharingLink(await _links.GetAllLinksForDepartmentAsync(departmentId) ?? new List<DepartmentLink>(), sponsorDepartmentId, departmentId,
				DepartmentLinkShareTypes.Training);
		}

		public async Task<List<LmsSponsorshipView>> GetForDepartmentAsync(int departmentId)
		{
			var views = new List<LmsSponsorshipView>();
			if (departmentId <= 0)
				return views;

			foreach (var sponsorship in await _sponsorships.GetForPartyAsync(departmentId, 100) ?? new List<LmsSponsorship>())
			{
				views.Add(new LmsSponsorshipView
				{
					Sponsorship = sponsorship,
					SponsorName = await NameAsync(sponsorship.SponsorDepartmentId),
					SponsoredName = await NameAsync(sponsorship.DepartmentId),
					SeatsUsed = sponsorship.IsActive ? await CountedSeatsAsync(sponsorship.DepartmentId) : 0,
					LinkOk = sponsorship.IsActive && await SharingLinkAsync(sponsorship.SponsorDepartmentId, sponsorship.DepartmentId, sponsorship.DepartmentLinkId) != null
				});
			}

			return views;
		}

		public async Task<LmsSponsorshipErrors> SponsorAsync(ContentSetActor sponsor, int sponsoredDepartmentId, int? seatLimit, CancellationToken cancellationToken = default)
		{
			if (sponsor == null || !sponsor.CanManage)
				return LmsSponsorshipErrors.NotAuthorized;
			if (sponsoredDepartmentId <= 0 || sponsoredDepartmentId == sponsor.DepartmentId || (seatLimit.HasValue && seatLimit.Value < 0))
				return LmsSponsorshipErrors.InvalidInput;

			var link = await SharingLinkAsync(sponsor.DepartmentId, sponsoredDepartmentId, null);
			if (link == null)
				return LmsSponsorshipErrors.NotShared;

			// The paid-plan rule applies to the department that pays: it needs its own entitled LMS add-on (and the operator flag).
			if (!await _access.IsFlagEnabledAsync(sponsor.DepartmentId) || !(await _access.GetSubscriptionAsync(sponsor.DepartmentId)).IsEntitled)
				return LmsSponsorshipErrors.SponsorNeedsLms;

			if (await _sponsorships.GetActiveAsync(sponsor.DepartmentId, sponsoredDepartmentId) != null)
				return LmsSponsorshipErrors.AlreadySponsored;

			LmsSponsorship sponsorship;
			try
			{
				sponsorship = await _sponsorships.InsertAsync(new LmsSponsorship
				{
					DepartmentId = sponsoredDepartmentId,
					SponsorDepartmentId = sponsor.DepartmentId,
					DepartmentLinkId = link.DepartmentLinkId,
					Status = (int)LmsSponsorshipStatuses.Active,
					SeatLimit = seatLimit,
					StartedOn = DateTime.UtcNow,
					StartedByUserId = sponsor.UserId
				}, cancellationToken, true);
			}
			catch (Exception ex) when (ex.Message.IndexOf("UX_LmsSponsorships_Active", StringComparison.OrdinalIgnoreCase) >= 0 ||
				ex.Message.IndexOf("ux_lmssponsorships_active", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return LmsSponsorshipErrors.AlreadySponsored;
			}

			await AfterChangeAsync(sponsorship, true, sponsor.UserId, cancellationToken);
			return LmsSponsorshipErrors.None;
		}

		public async Task<LmsSponsorshipErrors> SetSeatLimitAsync(ContentSetActor sponsor, int lmsSponsorshipId, int? seatLimit, CancellationToken cancellationToken = default)
		{
			if (sponsor == null || !sponsor.CanManage)
				return LmsSponsorshipErrors.NotAuthorized;
			if (seatLimit.HasValue && seatLimit.Value < 0)
				return LmsSponsorshipErrors.InvalidInput;
			var sponsorship = await _sponsorships.GetByIdAsync(lmsSponsorshipId);
			if (sponsorship == null || sponsorship.SponsorDepartmentId != sponsor.DepartmentId || !sponsorship.IsActive)
				return LmsSponsorshipErrors.NotFound;

			sponsorship.SeatLimit = seatLimit;
			await _sponsorships.UpdateAsync(sponsorship, cancellationToken, true);
			Audit(sponsor.DepartmentId, sponsor.UserId, new { sponsorship.LmsSponsorshipId, sponsorship.DepartmentId, sponsorship.SeatLimit, action = "seat limit" });
			return LmsSponsorshipErrors.None;
		}

		public async Task<LmsSponsorshipErrors> EndAsync(ContentSetActor actor, int lmsSponsorshipId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.CanManage)
				return LmsSponsorshipErrors.NotAuthorized;
			var sponsorship = await _sponsorships.GetByIdAsync(lmsSponsorshipId);
			if (sponsorship == null || !sponsorship.IsActive || (sponsorship.SponsorDepartmentId != actor.DepartmentId && sponsorship.DepartmentId != actor.DepartmentId))
				return LmsSponsorshipErrors.NotFound;

			var reason = sponsorship.SponsorDepartmentId == actor.DepartmentId ? LmsSponsorshipEndReasons.EndedBySponsor : LmsSponsorshipEndReasons.DeclinedBySponsored;
			if (!await _sponsorships.TryEndAsync(sponsorship.LmsSponsorshipId, reason, actor.UserId, DateTime.UtcNow, cancellationToken))
				return LmsSponsorshipErrors.NotFound;

			sponsorship = await _sponsorships.GetByIdAsync(lmsSponsorshipId) ?? sponsorship;
			await AfterChangeAsync(sponsorship, false, actor.UserId, cancellationToken);
			return LmsSponsorshipErrors.None;
		}

		public async Task<LmsSponsorshipGrant> GetGrantAsync(int departmentId)
		{
			if (departmentId <= 0)
				return null;

			foreach (var sponsorship in await _sponsorships.GetActiveForDepartmentAsync(departmentId) ?? new List<LmsSponsorship>())
			{
				if (await SharingLinkAsync(sponsorship.SponsorDepartmentId, departmentId, sponsorship.DepartmentLinkId) == null)
					continue;
				var subscription = await _access.GetSubscriptionAsync(sponsorship.SponsorDepartmentId);
				if (!subscription.IsEntitled)
					continue;

				var purchased = Math.Max(0, subscription.Packs) * PackSize;
				var own = await CountedSeatsAsync(sponsorship.SponsorDepartmentId);
				var learners = Math.Max(0, await _externalLearners.CountCountedAsync(sponsorship.SponsorDepartmentId));
				var external = LmsExternalLearnerRules.SeatsTaken(learners, purchased);
				var others = new List<(int, int?)>();
				foreach (var other in (await _sponsorships.GetActiveForSponsorAsync(sponsorship.SponsorDepartmentId) ?? new List<LmsSponsorship>())
					.Where(o => o.LmsSponsorshipId != sponsorship.LmsSponsorshipId))
					others.Add((await CountedSeatsAsync(other.DepartmentId), other.SeatLimit));
				var used = await CountedSeatsAsync(departmentId);

				return new LmsSponsorshipGrant
				{
					Sponsorship = sponsorship,
					SponsorName = await NameAsync(sponsorship.SponsorDepartmentId),
					SponsorPacks = subscription.Packs,
					Capacity = LmsSponsorshipRules.CapacityFor(purchased, own, external, LmsSponsorshipRules.SponsoredSeats(others), used, sponsorship.SeatLimit)
				};
			}

			return null;
		}

		public Task<Dictionary<int, DateTime?>> GetSponsoredCourseIdsAsync(int departmentId) => SponsoredCopiesAsync(departmentId, ContentSetItemKinds.LmsCourse);

		public Task<Dictionary<int, DateTime?>> GetSponsoredPathIdsAsync(int departmentId) => SponsoredCopiesAsync(departmentId, ContentSetItemKinds.LmsPath);

		private async Task<Dictionary<int, DateTime?>> SponsoredCopiesAsync(int departmentId, ContentSetItemKinds kind)
		{
			var courses = new Dictionary<int, DateTime?>();
			if (departmentId <= 0)
				return courses;

			var now = DateTime.UtcNow;
			var copies = (await _copies.GetManagedForDepartmentAsync(departmentId, kind) ?? new List<ContentSetCopy>()).ToLookup(c => c.PublisherDepartmentId);
			if (copies.Count == 0)
				return courses;

			foreach (var sponsorship in await _sponsorships.GetForPartyAsync(departmentId, 100) ?? new List<LmsSponsorship>())
			{
				if (sponsorship.DepartmentId != departmentId || !copies.Contains(sponsorship.SponsorDepartmentId))
					continue;

				DateTime? lapsedFrom;
				if (sponsorship.IsActive)
				{
					var linkOk = await SharingLinkAsync(sponsorship.SponsorDepartmentId, departmentId, sponsorship.DepartmentLinkId) != null;
					var subscription = await _access.GetSubscriptionAsync(sponsorship.SponsorDepartmentId);
					if (linkOk && subscription.IsEntitled)
						lapsedFrom = null;
					else if (linkOk && subscription.LapsedSinceUtc.HasValue)
						// The sponsor's add-on lapsed: the sponsored copies follow the sponsor's own lapse clock.
						lapsedFrom = subscription.LapsedSinceUtc.Value;
					else
						// The link stopped sharing (worker 100 ends it) or billing cannot say: nothing runs on an unknown footing.
						continue;
				}
				else
				{
					if (!sponsorship.EndedOn.HasValue || sponsorship.EndedOn.Value < now - LapseHistory)
						continue;
					lapsedFrom = sponsorship.EndedOn.Value;
				}

				foreach (var copy in copies[sponsorship.SponsorDepartmentId])
				{
					// A paying sponsorship wins over an older ended one for the same copy.
					if (!courses.TryGetValue(copy.LocalItemId, out var existing) || (existing.HasValue && (!lapsedFrom.HasValue || lapsedFrom.Value > existing.Value)))
						courses[copy.LocalItemId] = lapsedFrom;
				}
			}

			return courses;
		}

		public async Task<int> GetSponsoredSeatsAsync(int sponsorDepartmentId)
		{
			if (sponsorDepartmentId <= 0)
				return 0;
			var counted = new List<(int, int?)>();
			foreach (var sponsorship in await _sponsorships.GetActiveForSponsorAsync(sponsorDepartmentId) ?? new List<LmsSponsorship>())
				counted.Add((await CountedSeatsAsync(sponsorship.DepartmentId), sponsorship.SeatLimit));
			return LmsSponsorshipRules.SponsoredSeats(counted);
		}

		public async Task<int> EndLapsedAsync(int? departmentLinkId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var ended = 0;
			var active = departmentLinkId.HasValue
				? await _sponsorships.GetActiveForLinkAsync(departmentLinkId.Value) ?? new List<LmsSponsorship>()
				: await _sponsorships.GetAllActiveAsync(5000) ?? new List<LmsSponsorship>();

			foreach (var sponsorship in active)
			{
				try
				{
					var link = await SharingLinkAsync(sponsorship.SponsorDepartmentId, sponsorship.DepartmentId, sponsorship.DepartmentLinkId);
					if (link != null)
					{
						if (link.DepartmentLinkId != sponsorship.DepartmentLinkId)
						{
							// Another enabled link still shares Training: the sponsorship moves to it rather than ending.
							sponsorship.DepartmentLinkId = link.DepartmentLinkId;
							await _sponsorships.UpdateAsync(sponsorship, cancellationToken, true);
						}
						continue;
					}

					if (!await _sponsorships.TryEndAsync(sponsorship.LmsSponsorshipId, LmsSponsorshipEndReasons.LinkRevoked, null, utcNow, cancellationToken))
						continue;
					ended++;
					await AfterChangeAsync(await _sponsorships.GetByIdAsync(sponsorship.LmsSponsorshipId) ?? sponsorship, false, null, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"LMS sponsorship {sponsorship.LmsSponsorshipId} could not be checked against its link.");
				}
			}

			return ended;
		}

		private async Task AfterChangeAsync(LmsSponsorship sponsorship, bool started, string userId, CancellationToken cancellationToken)
		{
			Audit(sponsorship.SponsorDepartmentId, userId, new
			{
				sponsorship.LmsSponsorshipId, sponsorship.DepartmentId, sponsorship.SponsorDepartmentId, sponsorship.DepartmentLinkId, sponsorship.SeatLimit, sponsorship.EndReason,
				action = started ? "started" : "ended"
			});
			var sponsorName = await NameAsync(sponsorship.SponsorDepartmentId);
			var sponsoredName = await NameAsync(sponsorship.DepartmentId);
			var change = started ? LmsSponsorshipWorkflowChanges.Started : LmsSponsorshipWorkflowChanges.Ended;
			ContentSetWorkflowEvents.Sponsorship(_events, sponsorship.SponsorDepartmentId, change, sponsorship, sponsoredName, userId);
			ContentSetWorkflowEvents.Sponsorship(_events, sponsorship.DepartmentId, change, sponsorship, sponsorName, userId);
			try
			{
				// The sponsored department always sees who pays and when it stops.
				await _notifier.SponsorshipChangedAsync(sponsorship.DepartmentId, sponsorship.LmsSponsorshipId, sponsorship.SeatLimit ?? 0, started, sponsorName, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The sponsorship notice could not be sent.");
			}
		}

		private void Audit(int departmentId, string userId, object after) =>
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.LmsSponsorshipChanged,
				After = JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});
	}
}

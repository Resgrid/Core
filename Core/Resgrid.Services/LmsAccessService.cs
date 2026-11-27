using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// LMS add-on gate (protocols-training-knowledge-plan §17.3), cloned from ReadinessAccessService and EnhancedAiAccessService:
	/// the Training.Lms operator flag (seeded off by M0346; general availability is the operator switching it on), the Trainings
	/// module switch and a PaymentAddons window for PlanAddonTypes.Lms with no entitlement cache. Fails closed on any error.
	/// <para>
	/// Since wave 3 it also answers for the pack add-on: the packs of the running interval, a failed renewal inside the dunning
	/// grace (lapse rule 13, <see cref="LmsAddonConfig.DunningGraceDays"/>), and when a lapse began and ended, which it records in
	/// LmsAddonStates / LmsAddonLapses only when something changed. The Billing API reports only running intervals, so the last
	/// one seen is what tells a failed renewal from a cancelled or revoked one. Self-hosted (BillingApiBaseUrl unset): no add-on,
	/// the same as every other paid add-on; the synthesized free-forever payment never grants it.
	/// </para>
	/// Never called from a sweep (R-L9): an LMS worker queries for departments with due work first, then asks once per
	/// department per run; web navigation asks once per request (LmsRequestGate).
	/// </summary>
	public class LmsAccessService : ILmsAccessService
	{
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentSettingsService _settings;
		private readonly ISubscriptionsService _subscriptions;
		private readonly ILmsAddonStatesRepository _states;
		private readonly ILmsAddonLapsesRepository _lapses;
		private readonly ILmsMemberSeatsRepository _seats;
		private readonly IDepartmentsService _departments;
		private readonly IEventAggregator _events;
		// The sponsorship service asks this gate whether the sponsor's add-on is entitled; Lazy keeps the construction acyclic.
		private readonly Lazy<ILmsSponsorshipService> _sponsorships;

		public LmsAccessService(IFeatureToggleService flags, IDepartmentSettingsService settings, ISubscriptionsService subscriptions,
			ILmsAddonStatesRepository states, ILmsAddonLapsesRepository lapses, ILmsMemberSeatsRepository seats, IDepartmentsService departments,
			IEventAggregator events, Lazy<ILmsSponsorshipService> sponsorships)
		{
			_sponsorships = sponsorships;
			_flags = flags;
			_settings = settings;
			_subscriptions = subscriptions;
			_states = states;
			_lapses = lapses;
			_seats = seats;
			_departments = departments;
			_events = events;
		}

		public async Task<bool> IsFlagEnabledAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				if ((await _flags.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, departmentId))?.IsEnabled != true)
					return false;

				var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId, bypassCache: true);
				return modules != null && !modules.TrainingDisabled;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> CanAuthorAndManageAsync(int departmentId)
		{
			if (!await IsFlagEnabledAsync(departmentId))
				return false;

			return await GetActiveAddonStateAsync(departmentId) == true;
		}

		public async Task<bool> CanRunItemAsync(int departmentId, LmsRunnableOrigins origin, int? sponsorDepartmentId = null)
		{
			// R-S2 (A1): Resgrid system-published content (the Academy) runs for every department, add-on or not, and it is the one
			// LMS runtime that is not dark behind the Training.Lms flag: only the department's Trainings module switch closes it.
			if (origin == LmsRunnableOrigins.SystemPublished)
				return await IsTrainingModuleOnAsync(departmentId);

			if (!await IsFlagEnabledAsync(departmentId))
				return false;

			switch (origin)
			{

				// T5 item 2a / decision 8b (round 10 A3): a sponsored set runs on the sponsor's add-on through the Department Link: an active
				// sponsorship from that sponsor whose link still shares Training and whose sponsor's add-on is entitled.
				case LmsRunnableOrigins.Sponsored:
					if (!sponsorDepartmentId.HasValue || sponsorDepartmentId.Value <= 0 || _sponsorships?.Value == null)
						return false;
					try
					{
						var grant = await _sponsorships.Value.GetGrantAsync(departmentId);
						return grant?.Sponsorship != null && grant.Sponsorship.SponsorDepartmentId == sponsorDepartmentId.Value;
					}
					catch (Exception ex)
					{
						Framework.Logging.LogException(ex);
						return false;
					}

				default:
					return await GetActiveAddonStateAsync(departmentId) == true;
			}
		}

		public async Task<bool> CanBeTrainedAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId) || !await IsFlagEnabledAsync(departmentId))
				return false;

			try
			{
				var subscription = await GetSubscriptionAsync(departmentId);
				LmsSponsorshipGrant grant = null;
				if (!subscription.IsEntitled)
				{
					// T5 item 2a: a sponsored department's members are seated in the sponsor's packs.
					grant = _sponsorships?.Value == null ? null : await _sponsorships.Value.GetGrantAsync(departmentId);
					if (grant?.Sponsorship == null)
						return false;
				}

				var seats = await _seats.GetForDepartmentAsync(departmentId);
				if (seats == null || !seats.Any(s => s != null && string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase)))
					return false;

				var active = await _departments.GetActiveMemberUserIdsAsync(departmentId);
				return grant != null
					? Model.Lms.LmsSponsorshipRules.Licensed(seats, active, grant.Capacity).Contains(userId)
					: LmsSeatRules.Licensed(seats, active, subscription.Packs * LmsAddonConfig.PackSize, true).Contains(userId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		public async Task<HashSet<string>> GetLearnerSeatHoldersAsync(int departmentId)
		{
			var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (departmentId <= 0 || !await IsFlagEnabledAsync(departmentId))
				return none;

			try
			{
				var seats = await _seats.GetForDepartmentAsync(departmentId);
				if (seats == null || seats.Count == 0)
					return none;

				var subscription = await GetSubscriptionAsync(departmentId);
				int? capacity = null;
				if (subscription.IsEntitled)
				{
					capacity = subscription.Packs * LmsAddonConfig.PackSize;
				}
				else
				{
					// T5 item 2a: a sponsored department's members are seated in what the sponsor's packs leave it.
					var grant = _sponsorships?.Value == null ? null : await _sponsorships.Value.GetGrantAsync(departmentId);
					if (grant?.Sponsorship != null)
						capacity = grant.Capacity;
					else if (subscription.State == LmsSubscriptionStates.Lapsed && subscription.Packs > 0)
						// Lapse rule 1: the seats stay; who held one at the packs last paid for keeps the lapse windows (rules 4 and 11).
						capacity = subscription.Packs * LmsAddonConfig.PackSize;
					// Otherwise (billing cannot say, or a sponsorship stopped paying) no figure is known: every counted seat, the lapse
					// windows of the gate deciding what still runs.
				}

				var active = await _departments.GetActiveMemberUserIdsAsync(departmentId);
				return LmsSeatRules.LearnerSeatHolders(seats.Where(s => s != null && s.DepartmentId == departmentId), active, capacity);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"LMS learner seats could not be read for DepartmentId: {departmentId}");
				return none;
			}
		}

		/// <summary>The Trainings module is on (no flag, no billing). Fails closed.</summary>
		public async Task<bool> IsTrainingModuleOnAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId);
				return modules != null && !modules.TrainingDisabled;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		private static bool IsBillingConfigured() =>
			!string.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) &&
			!string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey);

		public async Task<bool?> GetActiveAddonStateAsync(int departmentId) => (await GetSubscriptionAsync(departmentId)).Entitlement;

		public async Task<LmsSubscriptionSnapshot> GetSubscriptionAsync(int departmentId)
		{
			if (departmentId <= 0)
				return LmsSubscriptionSnapshot.None;

			// Generic billing helpers synthesize free forever PTT payments when billing is unconfigured; a paid add-on must not
			// grant access through that fallback (Readiness Pro precedent). This is the self-hosted model of every paid add-on.
			if (!IsBillingConfigured())
				return LmsSubscriptionSnapshot.None;

			PaymentAddon current;
			try
			{
				var plans = await _subscriptions.GetAllAddonPlansByTypeAsync(PlanAddonTypes.Lms);
				var ids = plans?.Where(x => x != null && x.AddonType == (int)PlanAddonTypes.Lms && !string.IsNullOrWhiteSpace(x.PlanAddonId))
					.Select(x => x.PlanAddonId).Distinct().ToList();
				if (ids == null || ids.Count == 0)
					return LmsSubscriptionSnapshot.Unknown;

				// No entitlement cache: cancellation and renewal take effect on the next request.
				var payments = await _subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, ids);
				if (payments == null)
					return LmsSubscriptionSnapshot.Unknown;

				var now = DateTime.UtcNow;
				current = payments.Where(x => x != null && x.DepartmentId == departmentId && ids.Contains(x.PlanAddonId) &&
						x.EffectiveOn != default && x.EffectiveOn <= now && x.EndingOn > now &&
						!string.Equals(x.TransactionId, "SYSTEM", StringComparison.OrdinalIgnoreCase) && x.EndingOn != DateTime.MaxValue)
					.OrderByDescending(x => x.EndingOn).ThenByDescending(x => x.PurchaseOn).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return LmsSubscriptionSnapshot.Unknown;
			}

			return await ObserveAsync(departmentId, current, DateTime.UtcNow);
		}

		/// <summary>
		/// Turns one billing answer into the add-on's state, and records what changed: the packs and paid-through date of a running
		/// interval, a lapse that began (after the dunning grace for a failed renewal), and a lapse that ended. A failed state read or
		/// write never changes the answer for a running interval; without the stored state an ended interval simply has no grace.
		/// </summary>
		private async Task<LmsSubscriptionSnapshot> ObserveAsync(int departmentId, PaymentAddon current, DateTime now)
		{
			LmsAddonState state = null;
			try
			{
				state = await _states.GetForDepartmentAsync(departmentId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"LMS add-on state could not be read for DepartmentId: {departmentId}");
			}

			if (current != null)
			{
				// A row written before the pack path existed (or by a provider path that does not carry a quantity) counts as the floor.
				var packs = (int)Math.Clamp(current.Quantity, LmsAddonConfig.MinimumPacks, int.MaxValue);
				await RecordRunningAsync(departmentId, state, current, packs, now);
				return new LmsSubscriptionSnapshot
				{
					State = LmsSubscriptionStates.Active,
					Packs = packs,
					PaidThroughUtc = current.EndingOn,
					PeriodStartUtc = current.EffectiveOn,
					RenewalCancelled = current.IsCancelled
				};
			}

			if (state?.PaidThroughUtc == null)
				return LmsSubscriptionSnapshot.None;

			if (state.LapsedSinceUtc.HasValue)
				return Lapsed(state, state.LapsedSinceUtc.Value);

			var paidThrough = state.PaidThroughUtc.Value;
			DateTime lapseStart;
			LmsLapseReasons reason;
			if (now < paidThrough)
			{
				// The running interval disappeared before its end: refunded or charged back (the Billing API cuts it short).
				lapseStart = now;
				reason = LmsLapseReasons.Revoked;
			}
			else if (state.RenewalCancelled)
			{
				lapseStart = paidThrough;
				reason = LmsLapseReasons.RenewalCancelled;
			}
			else
			{
				// Lapse rule 13: a renewal that failed keeps working while the provider retries, the grace other monthly renewals get.
				var graceEnds = paidThrough.AddDays(Math.Max(0, LmsAddonConfig.DunningGraceDays));
				if (now < graceEnds)
					return new LmsSubscriptionSnapshot
					{
						State = LmsSubscriptionStates.DunningGrace,
						Packs = Math.Max(LmsAddonConfig.MinimumPacks, state.Packs),
						PaidThroughUtc = paidThrough,
						GraceEndsUtc = graceEnds
					};

				lapseStart = graceEnds;
				reason = LmsLapseReasons.PaymentFailed;
			}

			await RecordLapseAsync(departmentId, state, lapseStart, reason, now);
			return Lapsed(state, lapseStart);
		}

		private static LmsSubscriptionSnapshot Lapsed(LmsAddonState state, DateTime since) => new LmsSubscriptionSnapshot
		{
			State = LmsSubscriptionStates.Lapsed,
			Packs = state.Packs,
			PaidThroughUtc = state.PaidThroughUtc,
			RenewalCancelled = state.RenewalCancelled,
			LapsedSinceUtc = since
		};

		private async Task RecordRunningAsync(int departmentId, LmsAddonState state, PaymentAddon current, int packs, DateTime now)
		{
			try
			{
				var reactivated = state?.LapsedSinceUtc.HasValue == true;
				var changed = state == null || reactivated || state.Packs != packs || state.PaidThroughUtc != current.EndingOn ||
					state.RenewalCancelled != current.IsCancelled || !state.LastActiveOn.HasValue || state.LastActiveOn.Value < now.AddDays(-1);
				if (!changed)
					return;

				LmsAddonLapse closed = null;
				if (reactivated)
					// Lapse rule 12: the add-on is back; the closed lapse is what the reactivation re-base shifts paused due dates by.
					closed = await _lapses.CloseAsync(departmentId, now);

				// A renewal that changed the packs (not a reactivation, which raises its own event): workflow trigger 274, once, by the request
				// whose compare-and-set moved the stored packs.
				var previousPacks = state?.Packs;
				var packsChanged = state != null && !reactivated && state.Packs != packs && await _states.TryChangePacksAsync(departmentId, state.Packs, packs);

				var row = state ?? new LmsAddonState { DepartmentId = departmentId };
				row.Packs = packs;
				row.PaidThroughUtc = current.EndingOn;
				row.RenewalCancelled = current.IsCancelled;
				row.LastActiveOn = now;
				row.LapsedSinceUtc = null;
				row.UpdatedOn = now;
				await _states.SaveForDepartmentAsync(row);

				// Workflow trigger 268, once: only the request that closed the open lapse raises it (a concurrent one finds none open).
				if (closed != null)
					ContentWorkflowContext.Publish(_events, LmsWorkflowEvents.AddonReactivated(departmentId, closed, current.EndingOn, packs, now));
				if (packsChanged)
					ContentWorkflowContext.Publish(_events, LmsWorkflowEvents.SeatsChanged(departmentId, 0, 0, null, packs, previousPacks, null, now));
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"LMS add-on state could not be recorded for DepartmentId: {departmentId}");
			}
		}

		private async Task RecordLapseAsync(int departmentId, LmsAddonState state, DateTime lapseStart, LmsLapseReasons reason, DateTime now)
		{
			try
			{
				// Lapse rule 1: nothing is deleted. Seats stay enabled; the lapse is a period, not a cleanup.
				var opened = await _lapses.OpenAsync(new LmsAddonLapse
				{
					DepartmentId = departmentId,
					StartedOn = lapseStart,
					PaidThroughUtc = state.PaidThroughUtc,
					Reason = (int)reason,
					Packs = state.Packs,
					DetectedOn = now
				});

				state.LapsedSinceUtc = lapseStart;
				state.UpdatedOn = now;
				await _states.SaveForDepartmentAsync(state);

				// Workflow trigger 267, once: the filtered unique index lets only one request open the lapse.
				if (opened)
					ContentWorkflowContext.Publish(_events, LmsWorkflowEvents.AddonLapsed(departmentId, reason, lapseStart, state.PaidThroughUtc, state.Packs, now));
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"LMS add-on lapse could not be recorded for DepartmentId: {departmentId}");
			}
		}
	}
}

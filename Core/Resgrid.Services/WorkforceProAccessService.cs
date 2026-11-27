using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The Workforce Pro gate (shifts-scheduling-modernization-plan.md §7; owner 2026-11-26), cloned from LmsAccessService and
	/// IncidentBackOfficeAccessService: the fresh Workforce.Pro operator flag (seeded off; general availability is the operator switching it
	/// on), billing configured, a live PaymentAddons window for PlanAddonTypes.WorkforcePro with no entitlement cache, its pack quantity, and
	/// the seat holders licensed within packs × the pack size. Fails closed on any error: a flag that cannot be read is off, a billing answer
	/// that cannot be read is not licensed (read-only). Self-hosted (BillingApiBaseUrl unset): no add-on, the same as every other paid add-on;
	/// the synthesized free-forever payment never grants it.
	/// <para>
	/// One evaluation is remembered by the instance (one per request or worker run, InstancePerLifetimeScope) for <see cref="RememberFor"/>,
	/// so the many service calls of one page read billing once; a purchase, cancellation or seat change takes effect on the next request.
	/// </para>
	/// <para>
	/// Dunning grace (owner 2026-11-26, the LMS's lapse rule 13): a renewal that failed keeps Workforce Pro fully working, with the packs last
	/// paid for, for <see cref="WorkforceProAddonConfig.DunningGraceDays"/> (7) after its paid-through date while the provider retries the
	/// payment. The Billing API reports only running intervals, so the last one seen (WorkforceProAddonStates, written only when something
	/// changed) is what tells a failed renewal from a cancelled one (ends at its paid-through date) or a refunded or charged-back one (ends at
	/// once). After that, wellness is read-only.
	/// </para>
	/// </summary>
	public class WorkforceProAccessService : IWorkforceProAccessService
	{
		/// <summary>How long one instance remembers an evaluation (a request, or a short stretch of one worker run).</summary>
		public static readonly TimeSpan RememberFor = TimeSpan.FromSeconds(30);

		private readonly IFeatureToggleService _flags;
		private readonly ISubscriptionsService _subscriptions;
		private readonly IWorkforceProSeatsRepository _seats;
		private readonly IWorkforceProAddonStatesRepository _states;
		private readonly IDepartmentsService _departments;
		private readonly Dictionary<int, (DateTime At, WorkforceProAccess Access)> _remembered = new Dictionary<int, (DateTime, WorkforceProAccess)>();

		public WorkforceProAccessService(IFeatureToggleService flags, ISubscriptionsService subscriptions, IWorkforceProSeatsRepository seats,
			IWorkforceProAddonStatesRepository states, IDepartmentsService departments)
		{
			_flags = flags;
			_subscriptions = subscriptions;
			_seats = seats;
			_states = states;
			_departments = departments;
		}

		private static int PackSize => Math.Max(1, WorkforceProAddonConfig.PackSize);

		private static int MinimumPacks => Math.Max(1, WorkforceProAddonConfig.MinimumPacks);

		public async Task<bool> IsOfferedAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				return (await _flags.EvaluateFreshAsync(FeatureFlagKeys.WorkforcePro, departmentId))?.IsEnabled == true;
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

		public async Task<bool> HasActiveAddonAsync(int departmentId) => WorkforceProSubscription.IsEntitled((await ReadSubscriptionAsync(departmentId)).State);

		/// <summary>One answer from the billing read, after the stored state decides a failed renewal's grace.</summary>
		private readonly record struct Subscription(WorkforceProSubscriptionStates State, int Packs, DateTime? PaidThrough, bool Cancelled, DateTime? GraceEnds)
		{
			public static Subscription None => new Subscription(WorkforceProSubscriptionStates.None, 0, null, false, null);
			public static Subscription Unknown => new Subscription(WorkforceProSubscriptionStates.Unknown, 0, null, false, null);
		}

		/// <summary>
		/// The running interval, if any (its packs and paid-through date), or a failed renewal inside its dunning grace. Unknown when billing could
		/// not answer.
		/// </summary>
		private async Task<Subscription> ReadSubscriptionAsync(int departmentId)
		{
			if (departmentId <= 0 || !IsBillingConfigured())
				return Subscription.None;

			PaymentAddon current;
			try
			{
				var plans = await _subscriptions.GetAllAddonPlansByTypeAsync(PlanAddonTypes.WorkforcePro);
				var ids = plans?.Where(x => x != null && x.AddonType == (int)PlanAddonTypes.WorkforcePro && !string.IsNullOrWhiteSpace(x.PlanAddonId))
					.Select(x => x.PlanAddonId).Distinct().ToList();
				if (ids == null || ids.Count == 0)
					return Subscription.Unknown;

				// No entitlement cache: cancellation and renewal take effect on the next request.
				var payments = await _subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, ids);
				if (payments == null)
					return Subscription.Unknown;

				var now = DateTime.UtcNow;
				current = payments.Where(x => x != null && x.DepartmentId == departmentId && ids.Contains(x.PlanAddonId) &&
						x.EffectiveOn != default && x.EffectiveOn <= now && x.EndingOn > now &&
						!string.Equals(x.TransactionId, "SYSTEM", StringComparison.OrdinalIgnoreCase) && x.EndingOn != DateTime.MaxValue)
					.OrderByDescending(x => x.EndingOn).ThenByDescending(x => x.PurchaseOn).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return Subscription.Unknown;
			}

			return await ObserveAsync(departmentId, current, DateTime.UtcNow);
		}

		/// <summary>
		/// Turns one billing answer into the add-on's state (the LMS gate's ObserveAsync, lapse rule 13, without its lapse periods): a running
		/// interval is recorded; without one, the last interval seen decides. A renewal that failed keeps Workforce Pro working for
		/// <see cref="WorkforceProAddonConfig.DunningGraceDays"/> after its paid-through date; a cancelled renewal ends at that date; an interval
		/// that disappeared before its end (refunded or charged back) ends at once. A failed state read or write never changes the answer for a
		/// running interval; without the stored state an ended interval simply has no grace.
		/// </summary>
		private async Task<Subscription> ObserveAsync(int departmentId, PaymentAddon current, DateTime now)
		{
			WorkforceProAddonState state = null;
			try
			{
				state = await _states.GetForDepartmentAsync(departmentId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Workforce Pro add-on state could not be read for DepartmentId: {departmentId}");
			}

			if (current != null)
			{
				// A row written by a provider path that does not carry a quantity counts as the floor.
				var packs = (int)Math.Clamp(current.Quantity, MinimumPacks, int.MaxValue);
				await RecordRunningAsync(departmentId, state, current, packs, now);
				return new Subscription(WorkforceProSubscriptionStates.Active, packs, current.EndingOn, current.IsCancelled, null);
			}

			if (state?.PaidThroughUtc == null || state.LapsedSinceUtc.HasValue)
				return Subscription.None;

			var paidThrough = state.PaidThroughUtc.Value;
			DateTime endedOn;
			if (now < paidThrough)
				// The running interval disappeared before its end: refunded or charged back (the Billing API cuts it short).
				endedOn = now;
			else if (state.RenewalCancelled)
				endedOn = paidThrough;
			else
			{
				// A renewal that failed keeps working while the provider retries the payment (the LMS's lapse rule 13).
				var graceEnds = paidThrough.AddDays(Math.Max(0, WorkforceProAddonConfig.DunningGraceDays));
				if (now < graceEnds)
					return new Subscription(WorkforceProSubscriptionStates.DunningGrace, Math.Max(MinimumPacks, state.Packs), paidThrough, false, graceEnds);

				endedOn = graceEnds;
			}

			await RecordEndedAsync(departmentId, state, endedOn, now);
			return Subscription.None;
		}

		private async Task RecordRunningAsync(int departmentId, WorkforceProAddonState state, PaymentAddon current, int packs, DateTime now)
		{
			try
			{
				var changed = state == null || state.LapsedSinceUtc.HasValue || state.Packs != packs || state.PaidThroughUtc != current.EndingOn ||
					state.RenewalCancelled != current.IsCancelled || !state.LastActiveOn.HasValue || state.LastActiveOn.Value < now.AddDays(-1);
				if (!changed)
					return;

				var row = state ?? new WorkforceProAddonState { DepartmentId = departmentId };
				row.Packs = packs;
				row.PaidThroughUtc = current.EndingOn;
				row.RenewalCancelled = current.IsCancelled;
				row.LastActiveOn = now;
				row.LapsedSinceUtc = null;
				row.UpdatedOn = now;
				await _states.SaveForDepartmentAsync(row);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Workforce Pro add-on state could not be recorded for DepartmentId: {departmentId}");
			}
		}

		private async Task RecordEndedAsync(int departmentId, WorkforceProAddonState state, DateTime endedOn, DateTime now)
		{
			try
			{
				// Nothing is deleted: seats stay switched on and every record stays readable; only changes stop.
				state.LapsedSinceUtc = endedOn;
				state.UpdatedOn = now;
				await _states.SaveForDepartmentAsync(state);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Workforce Pro add-on end could not be recorded for DepartmentId: {departmentId}");
			}
		}

		public async Task<WorkforceProAccess> GetAccessAsync(int departmentId)
		{
			if (departmentId <= 0)
				return WorkforceProAccess.NotOffered(departmentId);

			var now = DateTime.UtcNow;
			if (_remembered.TryGetValue(departmentId, out var known) && now - known.At < RememberFor && now >= known.At)
				return known.Access;

			var access = await EvaluateAsync(departmentId);
			_remembered[departmentId] = (now, access);
			return access;
		}

		public void Invalidate(int departmentId) => _remembered.Remove(departmentId);

		private async Task<WorkforceProAccess> EvaluateAsync(int departmentId)
		{
			if (!await IsOfferedAsync(departmentId))
				return WorkforceProAccess.NotOffered(departmentId);

			var (state, packs, paidThrough, cancelled, graceEnds) = await ReadSubscriptionAsync(departmentId);
			var entitled = WorkforceProSubscription.IsEntitled(state);
			var holders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (entitled)
			{
				try
				{
					var seats = await _seats.GetForDepartmentAsync(departmentId) ?? new List<WorkforceProSeat>();
					var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					holders = WorkforceProSeatRules.Licensed(seats.Where(s => s != null && s.DepartmentId == departmentId), active, packs * PackSize, true);
				}
				catch (Exception ex)
				{
					// Fail closed: without the seat read nobody's records can be edited (they stay readable).
					Framework.Logging.LogException(ex, $"Workforce Pro seats could not be read for DepartmentId: {departmentId}");
					holders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				}
			}

			return new WorkforceProAccess
			{
				DepartmentId = departmentId,
				Offered = true,
				State = state,
				Packs = entitled ? packs : 0,
				PackSize = PackSize,
				PaidThroughUtc = paidThrough,
				RenewalCancelled = cancelled,
				GraceEndsUtc = graceEnds,
				SeatHolderUserIds = holders
			};
		}
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// Worker 91's three passes. Each starts from a query for due work (pending flags, departments with a due anniversary, departments
	/// holding an entry older than the 30-year minimum), never from a sweep of every department, and asks no billing gate (WL0 is free).
	/// Automation addresses active members only (inactive-member policy).
	/// </summary>
	public partial class MemberWellnessService
	{
		/// <summary>The legal-hold definition key an exposure entry answers to (a department-wide hold covers it too).</summary>
		public const string LegalHoldDefinitionKey = "responder-exposure";

		#region Post-call suggestions

		public async Task<WellnessRunResult> ProcessPendingFlagsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new WellnessRunResult();
			foreach (var flag in await _callFlags.GetPendingAsync(WorkerBatch) ?? Enumerable.Empty<RmsCallExposureFlag>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var call = await _callsService.GetCallByIdAsync(flag.CallId);
					var callGone = call == null || call.DepartmentId != flag.DepartmentId || call.IsDeleted;
					// "Post-call": a flag on a call still being worked waits for it to end, or for the delay to pass.
					if (!callGone && call.State == (int)CallStates.Active && utcNow - flag.FlaggedOn < MemberWellnessRules.OpenCallSuggestionDelay)
						continue;

					// Claim first: two worker hosts never build the same flag's suggestions.
					if (!await _callFlags.ClaimGenerationAsync(flag.DepartmentId, flag.RmsCallExposureFlagId, utcNow, cancellationToken))
						continue;

					result.FlagsProcessed++;
					try
					{
						var created = callGone ? 0 : await BuildSuggestionsAsync(flag, call, utcNow, cancellationToken);
						await _callFlags.SetSuggestionCountAsync(flag.DepartmentId, flag.RmsCallExposureFlagId, created, cancellationToken);
						result.SuggestionsCreated += created;
					}
					catch
					{
						await _callFlags.ReleaseGenerationAsync(flag.DepartmentId, flag.RmsCallExposureFlagId, CancellationToken.None);
						throw;
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Exposure suggestions failed for call flag {flag.RmsCallExposureFlagId} in department {flag.DepartmentId}.");
					result.Failures++;
				}
			}

			return result;
		}

		/// <summary>
		/// Suggests an entry to every active member the dispatch data places on the call who has no entry of the flag's category for it
		/// yet (a dismissed suggestion counts, so it is never suggested twice). Every row is <see cref="ResponderExposureStatuses.Suggested"/>.
		/// </summary>
		internal async Task<int> BuildSuggestionsAsync(RmsCallExposureFlag flag, Call call, DateTime utcNow, CancellationToken cancellationToken)
		{
			var departmentId = flag.DepartmentId;
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (active.Count == 0)
				return 0;

			var statuses = await _actionLogsService.Value.GetActionLogsForCallAsync(departmentId, call.CallId) ?? new List<ActionLog>();
			var unitStates = await _unitsService.Value.GetUnitStatesForCallAsync(departmentId, call.CallId) ?? new List<UnitState>();
			foreach (var state in unitStates.Where(s => s != null && (s.Roles == null || s.Roles.Count == 0) && s.UnitStateId > 0))
				state.Roles = await _unitsService.Value.GetRolesForUnitStateAsync(state.UnitStateId) ?? new List<UnitStateRole>();
			var checkIns = (await _checkInService.Value.GetCheckInsForCallAsync(call.CallId) ?? new List<CheckInRecord>()).Where(c => c.DepartmentId == departmentId).ToList();

			var candidates = ResponderExposureSuggestionBuilder.Build(statuses, unitStates, checkIns, active);
			if (candidates.Count == 0)
				return 0;

			var existing = (await _exposures.GetForCallAsync(departmentId, call.CallId) ?? Enumerable.Empty<RmsResponderExposure>())
				.Where(e => e.Category == flag.Category && e.Status != (int)ResponderExposureStatuses.Voided)
				.Select(e => e.UserId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var created = new List<RmsResponderExposure>();
			foreach (var candidate in candidates.Where(c => !existing.Contains(c.UserId)))
			{
				var row = new RmsResponderExposure
				{
					RmsResponderExposureId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					UserId = candidate.UserId,
					CallId = call.CallId,
					Category = flag.Category,
					OccurredOn = DateTime.SpecifyKind(candidate.StartedOn, DateTimeKind.Utc),
					DurationMinutes = candidate.DurationMinutes,
					Status = (int)ResponderExposureStatuses.Suggested,
					Source = (int)ResponderExposureSources.PostCallSuggestion,
					CallExposureFlagId = flag.RmsCallExposureFlagId,
					SuggestionBasis = string.Join(",", candidate.Bases),
					CreatedOn = utcNow,
					CreatedByUserId = flag.FlaggedByUserId,
					ModifiedOn = utcNow,
					ModifiedByUserId = flag.FlaggedByUserId,
					RowVersion = 1
				};
				await _exposures.InsertAsync(row, cancellationToken, true);
				created.Add(row);
			}

			await NotifySuggestionsAsync(departmentId, call, created);
			return created.Count;
		}

		private async Task NotifySuggestionsAsync(int departmentId, Call call, List<RmsResponderExposure> created)
		{
			if (created.Count == 0 || !ConfigHelper.CanTransmit(departmentId))
				return;
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				if (department == null)
					return;
				var departmentNumber = await _settingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
				var callDate = DateTime.SpecifyKind(call.LoggedOn, DateTimeKind.Utc).TimeConverter(department).ToShortDateString();
				const string title = "Please review your exposure log";
				var message = $"An entry for the call {call.Name} on {callDate} was suggested from dispatch records. Nothing is recorded until you review it in My exposures.";
				foreach (var userId in created.Select(c => c.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
					await _communicationService.Value.SendNotificationAsync(userId, departmentId, message, departmentNumber, department, title);
			}
			catch (Exception ex)
			{
				// The suggestions are saved; a failed notice must not undo or repeat them.
				Logging.LogException(ex, $"Exposure suggestion notices failed for department {departmentId}.");
			}
		}

		#endregion Post-call suggestions

		#region Anniversary reminders

		public async Task<int> SendAnniversaryRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var sent = 0;
			// The widest local "today" anywhere is a day ahead of UTC; the per-department check below decides.
			var dueBy = utcNow.Date.AddDays(MemberWellnessRules.AnniversaryLeadDays + 1);
			foreach (var departmentId in await _exposures.GetDepartmentsWithDueAnniversariesAsync(dueBy, WorkerBatch) ?? Enumerable.Empty<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					sent += await SendDepartmentRemindersAsync(departmentId, dueBy, utcNow, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Anniversary reminders failed for department {departmentId}.");
				}
			}

			return sent;
		}

		private async Task<int> SendDepartmentRemindersAsync(int departmentId, DateTime dueBy, DateTime utcNow, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;

			var localNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).TimeConverter(department);
			// Once a day, from the department's morning: a reminder about a hard anniversary should not arrive overnight.
			if (localNow.Hour < MemberWellnessRules.AnniversaryLocalHour)
				return 0;
			var localToday = localNow.Date;
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var canTransmit = ConfigHelper.CanTransmit(departmentId);
			List<string> peerTeam = null;
			Dictionary<string, string> names = null;
			string departmentNumber = null;
			var sent = 0;

			foreach (var row in await _exposures.GetDueAnniversariesAsync(departmentId, dueBy, WorkerBatch) ?? Enumerable.Empty<RmsResponderExposure>())
			{
				if (!row.NextAnniversaryOn.HasValue || !MemberWellnessRules.IsReminderDue(row.NextAnniversaryOn.Value, localToday))
					continue;

				var anniversary = row.NextAnniversaryOn.Value.Date;
				var localOccurred = DateTime.SpecifyKind(row.OccurredOn, DateTimeKind.Utc).TimeConverter(department).Date;
				var next = MemberWellnessRules.FollowingAnniversary(localOccurred, anniversary);

				// Claim first: a second run (or host) never sends the same anniversary twice.
				if (!await _exposures.ClaimAnniversaryAsync(departmentId, row.RmsResponderExposureId, row.NextAnniversaryOn.Value, next, utcNow, cancellationToken))
					continue;

				// A member who left, was disabled or hidden is not addressed, and nobody is told about them (inactive-member policy).
				if (!canTransmit || !row.Counts || !active.Contains(row.UserId))
					continue;

				var years = MemberWellnessRules.YearsSince(localOccurred, anniversary);
				var when = anniversary.ToString("D", CultureInfo.InvariantCulture);
				var yearText = years == 1 ? "one year" : years.ToString(CultureInfo.InvariantCulture) + " years";
				departmentNumber ??= await _settingsService.GetTextToCallNumberForDepartmentAsync(departmentId);

				if (row.RemindMember)
				{
					await _communicationService.Value.SendNotificationAsync(row.UserId, departmentId,
						$"{when} marks {yearText} since a difficult call you attended. Your peer support team and wellness resources are here whenever you would like them.",
						departmentNumber, department, "Thinking of you");
					sent++;
				}

				if (row.RemindPeerTeam)
				{
					peerTeam ??= await GetPeerTeamUserIdsAsync(departmentId);
					if (peerTeam.Count > 0)
					{
						names ??= (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
							.Where(n => !string.IsNullOrWhiteSpace(n.UserId))
							.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
							.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
						var name = names.TryGetValue(row.UserId, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : "A member";
						foreach (var peer in peerTeam.Where(p => !Same(p, row.UserId)))
						{
							await _communicationService.Value.SendNotificationAsync(peer, departmentId,
								$"{name} asked for the peer support team to know that {when} marks {yearText} since a critical incident they attended. A check-in may be welcome.",
								departmentNumber, department, "Peer support reminder");
							sent++;
						}
					}
				}
			}

			return sent;
		}

		#endregion Anniversary reminders

		#region Retention

		public async Task<int> EraseExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var erased = 0;
			var cutoff = utcNow.AddYears(-MemberWellnessRules.MinimumRetentionYears);
			foreach (var departmentId in await _exposures.GetDepartmentsWithEntriesBeforeAsync(cutoff, WorkerBatch) ?? Enumerable.Empty<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var settings = await GetSettingsAsync(departmentId);
					var holds = (await _legalHolds.GetActiveForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsRecordLegalHold>()).Where(h => h.IsActive).ToList();
					foreach (var row in await _exposures.GetBeforeAsync(departmentId, cutoff, WorkerBatch) ?? Enumerable.Empty<RmsResponderExposure>())
					{
						if (!MemberWellnessRules.IsPastRetention(row, settings, utcNow))
							continue;
						if (holds.Any(h => h.Covers(row.RmsResponderExposureId, LegalHoldDefinitionKey, row.OccurredOn)))
							continue;
						erased += await _exposures.DeleteForDepartmentAsync(departmentId, row.RmsResponderExposureId, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Exposure retention failed for department {departmentId}.");
				}
			}

			return erased;
		}

		#endregion Retention
	}
}

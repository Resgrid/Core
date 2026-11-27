using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// Worker 91's three passes. Each starts from a query for due work (pending flags, departments with a due anniversary, departments
	/// holding an entry older than the 30-year minimum), never from a sweep of every department. Automation addresses active members only
	/// (inactive-member policy). Workforce Pro (2026-11-26): the gate is asked once per department with due work (never in a sweep of every
	/// department). A department that is not licensed (or whose flag is off) gets no suggestions and no reminders: its due work is claimed and
	/// skipped, so nothing piles up to burst out later; in a licensed department only members holding a seat get suggestions and reminders.
	/// Retention erasure is a legal duty and runs whatever the add-on.
	/// </summary>
	public partial class MemberWellnessService
	{
		/// <summary>The legal-hold definition key an exposure entry answers to (a department-wide hold covers it too).</summary>
		public const string LegalHoldDefinitionKey = "responder-exposure";

		#region Post-call suggestions

		public async Task<WellnessRunResult> ProcessPendingFlagsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new WellnessRunResult();
			var gates = new Dictionary<int, WorkforceProAccess>();
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
						if (!gates.TryGetValue(flag.DepartmentId, out var gate))
							gates[flag.DepartmentId] = gate = await WorkforceProAsync(flag.DepartmentId);
						// Not licensed (lapsed since the flag was set, or the flag switched off): the flag is consumed without records.
						var created = callGone || !gate.Licensed ? 0 : await BuildSuggestionsAsync(flag, call, utcNow, cancellationToken, gate);
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
		internal async Task<int> BuildSuggestionsAsync(RmsCallExposureFlag flag, Call call, DateTime utcNow, CancellationToken cancellationToken, WorkforceProAccess gate = null)
		{
			var departmentId = flag.DepartmentId;
			gate ??= await WorkforceProAsync(departmentId);
			if (!gate.Licensed)
				return 0;
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (active.Count == 0)
				return 0;

			var statuses = await _actionLogsService.Value.GetActionLogsForCallAsync(departmentId, call.CallId) ?? new List<ActionLog>();
			var unitStates = await _unitsService.Value.GetUnitStatesForCallAsync(departmentId, call.CallId) ?? new List<UnitState>();
			foreach (var state in unitStates.Where(s => s != null && (s.Roles == null || s.Roles.Count == 0) && s.UnitStateId > 0))
				state.Roles = await _unitsService.Value.GetRolesForUnitStateAsync(state.UnitStateId) ?? new List<UnitStateRole>();
			var checkIns = (await _checkInService.Value.GetCheckInsForCallAsync(call.CallId) ?? new List<CheckInRecord>()).Where(c => c.DepartmentId == departmentId).ToList();

			// Records are created only for members holding a Workforce Pro seat.
			var candidates = ResponderExposureSuggestionBuilder.Build(statuses, unitStates, checkIns, active).Where(c => gate.HoldsSeat(c.UserId)).ToList();
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
				// Each member reads it in their own language, the call's date in the department's zone (round 9C).
				var cultures = new ContentNoticeCultures(_userProfiles, department);
				foreach (var userId in created.Select(c => c.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
				{
					var (culture, profile) = await cultures.ForMemberAsync(userId);
					var message = ContentNoticeText.Format("Wellness.Suggestion.Message", culture, call.Name, ContentNoticeCultures.LocalDate(call.LoggedOn, department, culture));
					await _communicationService.Value.SendNotificationAsync(userId, departmentId, message, departmentNumber, department,
						ContentNoticeText.Get("Wellness.Suggestion.Title", culture), profile);
				}
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
			// Workforce Pro: asked once for the department with due anniversaries.
			var gate = await WorkforceProAsync(departmentId);
			List<string> peerTeam = null;
			Dictionary<string, string> names = null;
			string departmentNumber = null;
			ContentNoticeCultures cultures = null;
			DepartmentTerminologyConfig terms = null;
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
				// No notices without Workforce Pro, and none about a member without a seat (claimed above, so nothing bursts out later).
				if (!gate.HoldsSeat(row.UserId))
					continue;

				var years = MemberWellnessRules.YearsSince(localOccurred, anniversary);
				departmentNumber ??= await _settingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
				cultures ??= new ContentNoticeCultures(_userProfiles, department);
				// The department's own word for its peer support team (and the other wellness terms), setting 118.
				terms ??= await TerminologyAsync(departmentId);

				if (row.RemindMember)
				{
					// The member's own language; the date written their way (round 9C).
					var (culture, profile) = await cultures.ForMemberAsync(row.UserId);
					await _communicationService.Value.SendNotificationAsync(row.UserId, departmentId, WellnessTerms.Apply(AnniversaryMessage(anniversary, years, culture), terms, culture),
						departmentNumber, department, WellnessTerms.Apply(ContentNoticeText.Get("Wellness.Anniversary.Title", culture), terms, culture), profile);
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
						var name = names.TryGetValue(row.UserId, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : null;
						foreach (var peer in peerTeam.Where(p => !Same(p, row.UserId)))
						{
							var (culture, profile) = await cultures.ForMemberAsync(peer);
							// The member's name goes in after the terms, so a name is never rewritten.
							var message = PeerReminderMessage(WellnessTerms.Apply(PeerReminderFormat(years, culture), terms, culture),
								name ?? ContentNoticeText.Get("Wellness.AMember", culture), anniversary, years, culture);
							await _communicationService.Value.SendNotificationAsync(peer, departmentId, message, departmentNumber, department,
								WellnessTerms.Apply(ContentNoticeText.Get("Wellness.PeerReminder.Title", culture), terms, culture), profile);
							sent++;
						}
					}
				}
			}

			return sent;
		}

		/// <summary>The member's anniversary reminder in <paramref name="culture"/>: the date written that way ("D"), one year or N years.</summary>
		public static string AnniversaryMessage(DateTime anniversary, int years, CultureInfo culture) =>
			years == 1 ? ContentNoticeText.Format("Wellness.Anniversary.OneYear", culture, anniversary)
				: ContentNoticeText.Format("Wellness.Anniversary.Years", culture, anniversary, years);

		/// <summary>The peer support team's reminder about <paramref name="memberName"/> in <paramref name="culture"/>.</summary>
		public static string PeerReminderMessage(string memberName, DateTime anniversary, int years, CultureInfo culture) =>
			years == 1 ? ContentNoticeText.Format("Wellness.PeerReminder.OneYear", culture, memberName, anniversary)
				: ContentNoticeText.Format("Wellness.PeerReminder.Years", culture, memberName, anniversary, years);

		/// <summary>
		/// The peer reminder's unformatted text ({0} the member, {1} the date, {2} the years): the department's terms are applied to it before
		/// the member's name goes in, so a name that happens to contain a term is never rewritten.
		/// </summary>
		public static string PeerReminderFormat(int years, CultureInfo culture) =>
			ContentNoticeText.Get(years == 1 ? "Wellness.PeerReminder.OneYear" : "Wellness.PeerReminder.Years", culture);

		/// <summary>A peer reminder format (<see cref="PeerReminderFormat"/>, terms applied) filled in; the text as it is when it cannot be.</summary>
		public static string PeerReminderMessage(string format, string memberName, DateTime anniversary, int years, CultureInfo culture)
		{
			try
			{
				return string.Format(culture ?? CultureInfo.InvariantCulture, format ?? string.Empty, memberName, anniversary, years);
			}
			catch (FormatException)
			{
				return format;
			}
		}

		/// <summary>The department's terminology (setting 118); empty when it cannot be read (labels only, never a lost notice).</summary>
		private async Task<DepartmentTerminologyConfig> TerminologyAsync(int departmentId)
		{
			try
			{
				return await _settingsService.GetDepartmentTerminologyAsync(departmentId) ?? new DepartmentTerminologyConfig();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Department terminology could not be read for the wellness notices of department {departmentId}.");
				return new DepartmentTerminologyConfig();
			}
		}

		#endregion Anniversary reminders

		#region Retention

		/// <summary>Never gated by Workforce Pro: erasing records past their legal retention is a duty, not a feature.</summary>
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

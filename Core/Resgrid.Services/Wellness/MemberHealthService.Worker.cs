using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// Worker 91's WL1 steps. Each starts from a query for due work (overdue Ryan White answers, injuries whose expected return passed,
	/// records older than their minimum retention), never from a sweep of every department. Workforce Pro (2026-11-26): the gate is asked once
	/// per department with due work; a department that is not licensed (or whose flag is off) gets no notices, and in a licensed department no
	/// notice is sent about a member without a seat (claimed and skipped, so nothing bursts out later). Retention erasure is never gated.
	/// Every notice is claimed before it is sent. A record about a member who left, was disabled or is hidden is claimed and then skipped:
	/// nobody is told about them (inactive-member policy), and recipients are active members only.
	/// </summary>
	public partial class MemberHealthService
	{
		#region Notices

		public async Task<int> SendDueNoticesAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var sent = 0;
			var activeByDepartment = new Dictionary<int, HashSet<string>>();
			var gates = new Dictionary<int, WorkforceProAccess>();

			async Task<bool> SeatedAsync(int departmentId, string userId)
			{
				if (!gates.TryGetValue(departmentId, out var gate))
					gates[departmentId] = gate = await WorkforceProAsync(departmentId);
				return gate.HoldsSeat(userId);
			}

			async Task<HashSet<string>> ActiveAsync(int departmentId)
			{
				if (!activeByDepartment.TryGetValue(departmentId, out var active))
				{
					active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					activeByDepartment[departmentId] = active;
				}
				return active;
			}

			foreach (var incident in await _incidents.GetOverdueNotificationsAsync(utcNow, WorkerBatch) ?? Enumerable.Empty<RmsExposureIncident>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (!ExposureIncidentWorkflow.IsNotificationOverdue(incident, utcNow))
						continue;
					// Claim first: two worker hosts never send the same overdue notice.
					if (!await _incidents.ClaimOverdueNoticeAsync(incident.DepartmentId, incident.RmsExposureIncidentId, utcNow, cancellationToken))
						continue;
					if (!(await ActiveAsync(incident.DepartmentId)).Contains(incident.UserId))
						continue;
					if (!await SeatedAsync(incident.DepartmentId, incident.UserId))
						continue;

					var department = await DepartmentAsync(incident.DepartmentId);
					DateTime? requested = incident.NotificationRequestedOn.HasValue ? LocalDate(incident.NotificationRequestedOn.Value, department) : null;
					var name = await NameOfAsync(incident.DepartmentId, incident.UserId);
					var caseLink = Link("/User/MemberHealth/Case?id=" + incident.RmsExposureIncidentId);
					sent += await NotifyAsync(incident.DepartmentId, await DesignatedOfficersAsync(incident.DepartmentId), "Health.NotificationOverdue.Title",
						requested.HasValue ? "Health.NotificationOverdue.MessageSent" : "Health.NotificationOverdue.Message", name,
						() => requested.HasValue ? new object[] { requested.Value, caseLink } : new object[] { caseLink },
						EventTypes.ExposureIncidentNotice, incident.RmsExposureIncidentId, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Overdue exposure notice failed for case {incident.RmsExposureIncidentId} in department {incident.DepartmentId}.");
				}
			}

			foreach (var injury in await _injuries.GetDueReturnReviewsAsync(utcNow, WorkerBatch) ?? Enumerable.Empty<RmsMemberInjury>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (!injury.IsOpen || !injury.ExpectedReturnOn.HasValue)
						continue;
					if (!await _injuries.ClaimReturnReviewAsync(injury.DepartmentId, injury.RmsMemberInjuryId, injury.ReturnReviewNoticeOn, utcNow, cancellationToken))
						continue;
					if (!(await ActiveAsync(injury.DepartmentId)).Contains(injury.UserId))
						continue;
					if (!await SeatedAsync(injury.DepartmentId, injury.UserId))
						continue;

					var name = await NameOfAsync(injury.DepartmentId, injury.UserId);
					var expected = injury.ExpectedReturnOn.Value;
					sent += await NotifyAsync(injury.DepartmentId, await HealthOfficersAsync(injury.DepartmentId), "Health.ReturnReview.Title", "Health.ReturnReview.Message", name,
						() => new object[] { expected, Link("/User/MemberHealth/Injury?id=" + injury.RmsMemberInjuryId) },
						EventTypes.DutyStatusNotice, injury.RmsMemberInjuryId, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Return-to-duty review notice failed for injury {injury.RmsMemberInjuryId} in department {injury.DepartmentId}.");
				}
			}

			return sent;
		}

		#endregion Notices

		#region Retention

		public async Task<int> EraseExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var erased = 0;
			var holdsByDepartment = new Dictionary<int, List<RmsRecordLegalHold>>();
			var settingsByDepartment = new Dictionary<int, WellnessSettings>();

			async Task<List<RmsRecordLegalHold>> HoldsAsync(int departmentId)
			{
				if (!holdsByDepartment.TryGetValue(departmentId, out var holds))
				{
					holds = (await _legalHolds.GetActiveForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsRecordLegalHold>()).Where(h => h.IsActive).ToList();
					holdsByDepartment[departmentId] = holds;
				}
				return holds;
			}

			async Task<WellnessSettings> SettingsAsync(int departmentId)
			{
				if (!settingsByDepartment.TryGetValue(departmentId, out var settings))
				{
					settings = await _wellness.GetSettingsAsync(departmentId) ?? new WellnessSettings();
					settingsByDepartment[departmentId] = settings;
				}
				return settings;
			}

			// Exposure cases: the WL0 30-year minimum (the department may keep them longer).
			var caseCutoff = utcNow.AddYears(-MemberWellnessRules.MinimumRetentionYears);
			foreach (var departmentId in await _incidents.GetDepartmentsWithCasesBeforeAsync(caseCutoff, WorkerBatch) ?? Enumerable.Empty<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var years = MemberWellnessRules.RetentionYears(await SettingsAsync(departmentId));
					var holds = await HoldsAsync(departmentId);
					foreach (var row in await _incidents.GetBeforeAsync(departmentId, caseCutoff, WorkerBatch) ?? Enumerable.Empty<RmsExposureIncident>())
					{
						if (row.OccurredOn.AddYears(years) >= utcNow)
							continue;
						if (holds.Any(h => h.Covers(row.RmsExposureIncidentId, MemberHealthRules.ExposureIncidentHoldKey, row.OccurredOn)))
							continue;
						foreach (var task in await FollowUpsForCaseAsync(departmentId, row.RmsExposureIncidentId))
							await _assignments.DeleteAsync(task, cancellationToken);
						erased += await _incidents.DeleteForDepartmentAsync(departmentId, row.RmsExposureIncidentId, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Exposure case retention failed for department {departmentId}.");
				}
			}

			// Injuries: five years after the end of their year (OSHA 1904.33), or the department's longer setting.
			var beforeYear = utcNow.Year - OshaRules.MinimumRetentionYears;
			foreach (var departmentId in await _injuries.GetDepartmentsWithInjuriesBeforeYearAsync(beforeYear, WorkerBatch) ?? Enumerable.Empty<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var years = (await SettingsAsync(departmentId)).Normalized().InjuryRetentionYears;
					var holds = await HoldsAsync(departmentId);
					foreach (var row in await _injuries.GetBeforeYearAsync(departmentId, beforeYear, WorkerBatch) ?? Enumerable.Empty<RmsMemberInjury>())
					{
						if (!OshaRules.IsPastRetention(row, years, utcNow))
							continue;
						if (holds.Any(h => h.Covers(row.RmsMemberInjuryId, MemberHealthRules.InjuryHoldKey, row.OccurredOn)))
							continue;
						await _periods.DeleteForInjuryAsync(departmentId, row.RmsMemberInjuryId, cancellationToken);
						erased += await _injuries.DeleteForDepartmentAsync(departmentId, row.RmsMemberInjuryId, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Injury retention failed for department {departmentId}.");
				}
			}

			// Medical clearances are employee medical records: the 30-year minimum (OSHA 1910.1020), as exposure records.
			var clearanceCutoff = utcNow.AddYears(-MemberWellnessRules.MinimumRetentionYears);
			foreach (var departmentId in await _clearances.GetDepartmentsWithClearancesBeforeAsync(clearanceCutoff, WorkerBatch) ?? Enumerable.Empty<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var years = MemberWellnessRules.RetentionYears(await SettingsAsync(departmentId));
					var holds = await HoldsAsync(departmentId);
					foreach (var row in await _clearances.GetBeforeAsync(departmentId, clearanceCutoff, WorkerBatch) ?? Enumerable.Empty<RmsMedicalClearance>())
					{
						if (row.EvaluatedOn.AddYears(years) >= utcNow)
							continue;
						if (holds.Any(h => h.Covers(row.RmsMedicalClearanceId, MemberHealthRules.ClearanceHoldKey, row.EvaluatedOn)))
							continue;
						erased += await _clearances.DeleteForDepartmentAsync(departmentId, row.RmsMedicalClearanceId, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Medical clearance retention failed for department {departmentId}.");
				}
			}

			return erased;
		}

		#endregion Retention
	}
}

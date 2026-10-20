using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>Community risk reduction activities (RMS plan section 4.3, RMS-5). Counts only; no attendee identity is stored.</summary>
	public class RecordsCrrService : IRecordsCrrService
	{
		private readonly RecordsPreventionGate _gate;
		private readonly IRmsCrrActivitiesRepository _activities;
		private readonly IRmsCrrAlarmRequestsRepository _requests;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly ICallsRepository _calls;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsCrrService(RecordsPreventionGate gate, IRmsCrrActivitiesRepository activities, IRmsCrrAlarmRequestsRepository requests, IRmsOccupanciesRepository occupancies,
			ICallsRepository calls, IRecordsAuthorizationService authorization, IUnitOfWork unitOfWork)
		{
			_gate = gate; _activities = activities; _requests = requests; _occupancies = occupancies; _calls = calls; _authorization = authorization; _unitOfWork = unitOfWork;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Crr);
		private async Task RequireViewAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Crr); await _gate.RequireViewerAsync(departmentId, userId); }
		private async Task RequireAdminAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Crr); await _gate.RequireAdminAsync(departmentId, userId); }
		private static void RequireStorableWindow(DateTime startUtc, DateTime endUtc) { RecordsPreventionGate.RequireStorableDate(startUtc, "The start date is not valid."); RecordsPreventionGate.RequireStorableDate(endUtc, "The end date is not valid."); }

		public async Task<List<RmsCrrActivity>> ListAsync(int departmentId, string userId, DateTime startUtc, DateTime endUtc, int take)
		{
			await RequireViewAsync(departmentId, userId);
			RequireStorableWindow(startUtc, endUtc);
			return (await _activities.GetForRangeAsync(departmentId, startUtc, endUtc, take))?.ToList() ?? new List<RmsCrrActivity>();
		}

		public async Task<List<RmsCrrActivity>> GetForOccupancyAsync(int departmentId, string userId, string occupancyId)
		{
			await RequireViewAsync(departmentId, userId);
			if (string.IsNullOrWhiteSpace(occupancyId)) return new List<RmsCrrActivity>();
			return ((await _activities.GetForOccupancyAsync(departmentId, occupancyId)) ?? Enumerable.Empty<RmsCrrActivity>()).Where(a => a != null && a.DeletedOn == null).ToList();
		}

		public async Task<RmsCrrActivity> GetAsync(int departmentId, string userId, string activityId)
		{
			await RequireViewAsync(departmentId, userId);
			var activity = await _activities.GetByIdForDepartmentAsync(departmentId, activityId);
			return activity == null || activity.DeletedOn != null ? null : activity;
		}

		public async Task<RmsCrrActivity> SaveAsync(int departmentId, string userId, RmsCrrActivity input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (!string.IsNullOrWhiteSpace(input.RmsOccupancyId))
				await CrrAlarmRules.RequireOccupancyAsync(_occupancies, departmentId, input.RmsOccupancyId);
			var now = DateTime.UtcNow;
			var entity = string.IsNullOrWhiteSpace(input.RmsCrrActivityId) ? null : await _activities.GetByIdForDepartmentAsync(departmentId, input.RmsCrrActivityId);
			if (entity != null && entity.DeletedOn != null) entity = null;
			var isNew = entity == null;
			// A call link is checked when it is set or changed; one the user can no longer see stays as it was.
			if (input.CallId.HasValue && (isNew || entity.CallId != input.CallId))
				await CrrAlarmRules.RequireCallAsync(_calls, _authorization, departmentId, userId, input.CallId.Value);
			if (isNew) entity = new RmsCrrActivity { RmsCrrActivityId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), CreatedOn = now, CreatedByUserId = userId, RowVersion = 0 };
			entity.Kind = input.Kind == 0 ? (int)RmsCrrActivityKind.PublicEducation : input.Kind;
			entity.OccurredOn = RecordsPreventionGate.RequireStorableDate(input.OccurredOn == default ? now : input.OccurredOn, "The activity date is not valid.");
			entity.Title = RecordsPreventionGate.Require(input.Title, 250, "An activity needs a title.");
			entity.Description = RecordsPreventionGate.Trim(input.Description, 4000); entity.RmsOccupancyId = RecordsPreventionGate.Trim(input.RmsOccupancyId, 36);
			entity.LocationText = RecordsPreventionGate.Trim(input.LocationText, 500); entity.Latitude = input.Latitude; entity.Longitude = input.Longitude;
			entity.AudienceCount = Math.Max(0, input.AudienceCount); entity.HoursSpent = Math.Max(0, input.HoursSpent);
			entity.SmokeAlarmsInstalled = CrrAlarmRules.Units(input.SmokeAlarmsInstalled, "Smoke alarms");
			entity.CoAlarmsInstalled = CrrAlarmRules.Units(input.CoAlarmsInstalled, "CO alarms");
			entity.ComboAlarmsInstalled = CrrAlarmRules.Units(input.ComboAlarmsInstalled, "Combination alarms");
			entity.AlarmReason = entity.AlarmsInstalledTotal() == 0 ? (int)RmsAlarmReason.Unspecified : CrrAlarmRules.Reason(input.AlarmReason);
			entity.CallId = input.CallId;
			entity.StaffUserIdsCsv = RecordsPreventionGate.Trim(input.StaffUserIdsCsv, 4000); entity.Outcome = RecordsPreventionGate.Trim(input.Outcome, 4000); entity.NerisSecondaryJson = RecordsPreventionGate.Trim(input.NerisSecondaryJson, 8000);
			entity.ModifiedOn = now; entity.RowVersion++;
			if (isNew) await _activities.InsertAsync(entity, cancellationToken, true); else await _activities.UpdateAsync(entity, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, isNew ? "CRR activity recorded" : "CRR activity updated", entity.RmsCrrActivityId,
				new { entity.Title, kind = ((RmsCrrActivityKind)entity.Kind).ToString(), entity.AudienceCount, smoke = entity.SmokeAlarmsInstalled, co = entity.CoAlarmsInstalled, combo = entity.ComboAlarmsInstalled }, cancellationToken: cancellationToken);
			return entity;
		}

		public async Task DeleteAsync(int departmentId, string userId, string activityId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var activity = await _activities.GetByIdForDepartmentAsync(departmentId, activityId);
			if (activity == null || activity.DeletedOn != null) throw new ArgumentException("The activity does not exist.");
			var now = DateTime.UtcNow;
			var request = string.IsNullOrWhiteSpace(activity.RmsCrrAlarmRequestId) ? null : await _requests.GetByIdForDepartmentAsync(departmentId, activity.RmsCrrAlarmRequestId);
			if (request != null && (request.DeletedOn != null || request.RmsCrrActivityId != activity.RmsCrrActivityId)) request = null;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				activity.DeletedOn = now; activity.ModifiedOn = now; activity.RowVersion++;
				await _activities.UpdateAsync(activity, cancellationToken, true);
				// The units it recorded are gone, so the request it completed is outstanding again.
				if (request != null)
				{
					if (!await _requests.TryBumpRowVersionAsync(departmentId, request.RmsCrrAlarmRequestId, request.RowVersion, cancellationToken))
						throw new InvalidOperationException("The alarm request for this activity was changed by someone else; reload and try again.");
					request.Status = (int)RmsAlarmRequestStatus.Open; request.ClosedOn = null; request.ClosedByUserId = null; request.RmsCrrActivityId = null;
					request.ModifiedOn = now; request.ModifiedByUserId = userId; request.RowVersion++;
					await _requests.UpdateAsync(request, cancellationToken, true);
				}
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "CRR activity removed", activityId, new { activity.Title, reopenedRequest = request?.RequestNumber }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
		}

		public async Task<CrrSummary> GetSummaryAsync(int departmentId, string userId, DateTime startUtc, DateTime endUtc)
		{
			await RequireViewAsync(departmentId, userId);
			RequireStorableWindow(startUtc, endUtc);
			var rows = (await _activities.GetForRangeAsync(departmentId, startUtc, endUtc, 2000))?.ToList() ?? new List<RmsCrrActivity>();
			var summary = new CrrSummary
			{
				Start = startUtc, End = endUtc, Activities = rows.Count, Audience = rows.Sum(r => r.AudienceCount), Hours = rows.Sum(r => r.HoursSpent),
				SmokeAlarmsInstalled = rows.Sum(r => r.SmokeAlarmsInstalled), CoAlarmsInstalled = rows.Sum(r => r.CoAlarmsInstalled), ComboAlarmsInstalled = rows.Sum(r => r.ComboAlarmsInstalled),
				AlarmsInstalled = rows.Sum(r => r.AlarmsInstalledTotal()), AlarmVisits = rows.Count(r => r.AlarmsInstalledTotal() > 0)
			};
			foreach (var group in rows.GroupBy(r => r.Kind)) summary.ByKind[group.Key] = group.Count();
			foreach (var group in rows.Where(r => r.AlarmsInstalledTotal() > 0).GroupBy(r => r.AlarmReason)) summary.AlarmsByReason[group.Key] = group.Sum(r => r.AlarmsInstalledTotal());
			summary.OpenAlarmRequests = await _requests.CountAsync(departmentId, new RmsAlarmRequestQuery { Statuses = new List<int> { (int)RmsAlarmRequestStatus.Open } });
			foreach (var status in Enum.GetValues(typeof(RmsAlarmRequestStatus)).Cast<int>())
			{
				var count = await _requests.CountAsync(departmentId, new RmsAlarmRequestQuery { Statuses = new List<int> { status }, RequestedFrom = startUtc, RequestedBefore = endUtc });
				if (count > 0) summary.AlarmRequestsByStatus[status] = count;
			}
			return summary;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>Community risk reduction activities (RMS plan section 4.3, RMS-5). Counts only; no attendee identity is stored.</summary>
	public class RecordsCrrService : IRecordsCrrService
	{
		private readonly RecordsPreventionGate _gate;
		private readonly IRmsCrrActivitiesRepository _activities;
		private readonly IRmsOccupanciesRepository _occupancies;

		public RecordsCrrService(RecordsPreventionGate gate, IRmsCrrActivitiesRepository activities, IRmsOccupanciesRepository occupancies)
		{
			_gate = gate; _activities = activities; _occupancies = occupancies;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Crr);
		private async Task RequireViewAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Crr); await _gate.RequireViewerAsync(departmentId, userId); }
		private async Task RequireAdminAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Crr); await _gate.RequireAdminAsync(departmentId, userId); }

		public async Task<List<RmsCrrActivity>> ListAsync(int departmentId, string userId, DateTime startUtc, DateTime endUtc, int take)
		{
			await RequireViewAsync(departmentId, userId);
			return (await _activities.GetForRangeAsync(departmentId, startUtc, endUtc, take))?.ToList() ?? new List<RmsCrrActivity>();
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
			{
				var occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, input.RmsOccupancyId);
				if (occupancy == null || occupancy.DeletedOn != null) throw new ArgumentException("The occupancy does not exist.");
			}
			var now = DateTime.UtcNow;
			var entity = string.IsNullOrWhiteSpace(input.RmsCrrActivityId) ? null : await _activities.GetByIdForDepartmentAsync(departmentId, input.RmsCrrActivityId);
			if (entity != null && entity.DeletedOn != null) entity = null;
			var isNew = entity == null;
			if (isNew) entity = new RmsCrrActivity { RmsCrrActivityId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), CreatedOn = now, CreatedByUserId = userId, RowVersion = 0 };
			entity.Kind = input.Kind == 0 ? (int)RmsCrrActivityKind.PublicEducation : input.Kind;
			entity.OccurredOn = input.OccurredOn == default ? now : input.OccurredOn;
			entity.Title = RecordsPreventionGate.Require(input.Title, 250, "An activity needs a title.");
			entity.Description = RecordsPreventionGate.Trim(input.Description, 4000); entity.RmsOccupancyId = RecordsPreventionGate.Trim(input.RmsOccupancyId, 36);
			entity.LocationText = RecordsPreventionGate.Trim(input.LocationText, 500); entity.Latitude = input.Latitude; entity.Longitude = input.Longitude;
			entity.AudienceCount = Math.Max(0, input.AudienceCount); entity.SmokeAlarmsInstalled = Math.Max(0, input.SmokeAlarmsInstalled); entity.HoursSpent = Math.Max(0, input.HoursSpent);
			entity.StaffUserIdsCsv = RecordsPreventionGate.Trim(input.StaffUserIdsCsv, 4000); entity.Outcome = RecordsPreventionGate.Trim(input.Outcome, 4000); entity.NerisSecondaryJson = RecordsPreventionGate.Trim(input.NerisSecondaryJson, 8000);
			entity.ModifiedOn = now; entity.RowVersion++;
			if (isNew) await _activities.InsertAsync(entity, cancellationToken, true); else await _activities.UpdateAsync(entity, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, isNew ? "CRR activity recorded" : "CRR activity updated", entity.RmsCrrActivityId, new { entity.Title, kind = ((RmsCrrActivityKind)entity.Kind).ToString(), entity.AudienceCount }, cancellationToken: cancellationToken);
			return entity;
		}

		public async Task DeleteAsync(int departmentId, string userId, string activityId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var activity = await _activities.GetByIdForDepartmentAsync(departmentId, activityId);
			if (activity == null || activity.DeletedOn != null) throw new ArgumentException("The activity does not exist.");
			activity.DeletedOn = DateTime.UtcNow; activity.ModifiedOn = activity.DeletedOn.Value; activity.RowVersion++;
			await _activities.UpdateAsync(activity, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "CRR activity removed", activityId, new { activity.Title }, cancellationToken: cancellationToken);
		}

		public async Task<CrrSummary> GetSummaryAsync(int departmentId, string userId, DateTime startUtc, DateTime endUtc)
		{
			await RequireViewAsync(departmentId, userId);
			var rows = (await _activities.GetForRangeAsync(departmentId, startUtc, endUtc, 2000))?.ToList() ?? new List<RmsCrrActivity>();
			var summary = new CrrSummary { Start = startUtc, End = endUtc, Activities = rows.Count, Audience = rows.Sum(r => r.AudienceCount), SmokeAlarmsInstalled = rows.Sum(r => r.SmokeAlarmsInstalled), Hours = rows.Sum(r => r.HoursSpent) };
			foreach (var group in rows.GroupBy(r => r.Kind)) summary.ByKind[group.Key] = group.Count();
			return summary;
		}
	}
}

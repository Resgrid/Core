using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The annual training plan (§13 T3 item 3a, R-C5; LMS add-on): plans per year for the department, a group or a shift; planned lines
	/// per category and month; the dashboard of planned against actual from the T1 ledger with the forecast against the category targets;
	/// and "make it a calendar item". Consumes baseline services only (§17.3); its callers check the LMS gate.
	/// </summary>
	public class LmsTrainingPlanService : ILmsTrainingPlanService
	{
		private const int LedgerPage = 5000;
		private const int LedgerCap = 200000;

		private readonly ILmsTrainingPlansRepository _plans;
		private readonly ILmsTrainingPlanItemsRepository _items;
		private readonly ILearningRecordService _learningRecords;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IShiftsService _shifts;
		private readonly ICalendarService _calendar;
		private readonly IEventAggregator _events;

		public LmsTrainingPlanService(ILmsTrainingPlansRepository plans, ILmsTrainingPlanItemsRepository items, ILearningRecordService learningRecords,
			IDepartmentsService departments, IDepartmentGroupsService groups, IShiftsService shifts, ICalendarService calendar, IEventAggregator events)
		{
			_plans = plans;
			_items = items;
			_learningRecords = learningRecords;
			_departments = departments;
			_groups = groups;
			_shifts = shifts;
			_calendar = calendar;
			_events = events;
		}

		#region Plans and lines

		public async Task<List<LmsTrainingPlan>> GetPlansAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsTrainingPlan>() : await _plans.GetForDepartmentAsync(departmentId) ?? new List<LmsTrainingPlan>();

		public async Task<LmsTrainingPlan> GetPlanAsync(int departmentId, int lmsTrainingPlanId)
		{
			var plan = await _plans.GetByIdForDepartmentAsync(departmentId, lmsTrainingPlanId);
			return plan == null || plan.IsDeleted || plan.DepartmentId != departmentId ? null : plan;
		}

		public async Task<List<LmsTrainingPlanItem>> GetItemsAsync(int departmentId, int lmsTrainingPlanId) =>
			(await _items.GetForPlanAsync(departmentId, lmsTrainingPlanId) ?? new List<LmsTrainingPlanItem>()).Where(i => i.DepartmentId == departmentId).ToList();

		private async Task<LmsTrainingPlan> RequirePlanAsync(int departmentId, int lmsTrainingPlanId) =>
			await GetPlanAsync(departmentId, lmsTrainingPlanId) ?? throw new InvalidOperationException("lms_program_plan_not_found");

		public async Task<LmsTrainingPlan> SavePlanAsync(LmsTrainingPlan plan, string userId, CancellationToken cancellationToken = default)
		{
			if (plan == null || plan.DepartmentId <= 0)
				throw new InvalidOperationException("lms_program_plan_not_found");
			var name = Trim(plan.Name, LmsTrainingPlan.NameMaxLength);
			if (string.IsNullOrWhiteSpace(name))
				throw new InvalidOperationException("lms_program_name_required");
			if (plan.Year < 2000 || plan.Year > 2100)
				throw new InvalidOperationException("lms_program_year_invalid");
			if (!Enum.IsDefined(typeof(LmsTrainingPlanScopes), plan.ScopeKind))
				throw new InvalidOperationException("lms_program_scope_invalid");
			if (plan.ScopeKind != (int)LmsTrainingPlanScopes.Department && !await IsScopeOfDepartmentAsync(plan.DepartmentId, (LmsTrainingPlanScopes)plan.ScopeKind, plan.ScopeId))
				throw new InvalidOperationException("lms_program_scope_invalid");

			var now = DateTime.UtcNow;
			LmsTrainingPlan stored;
			string before = null;
			if (plan.LmsTrainingPlanId > 0)
			{
				stored = await RequirePlanAsync(plan.DepartmentId, plan.LmsTrainingPlanId);
				before = JsonConvert.SerializeObject(stored);
				stored.UpdatedOn = now;
				stored.UpdatedByUserId = userId;
			}
			else
			{
				stored = new LmsTrainingPlan { DepartmentId = plan.DepartmentId, Status = (int)LmsTrainingPlanStatuses.Draft, CreatedOn = now, CreatedByUserId = userId };
			}

			stored.Name = name;
			stored.Year = plan.Year;
			stored.ScopeKind = plan.ScopeKind;
			stored.ScopeId = plan.ScopeKind == (int)LmsTrainingPlanScopes.Department ? null : plan.ScopeId;
			stored.Notes = Trim(plan.Notes, LmsTrainingPlan.NotesMaxLength);
			var saved = await _plans.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(saved.DepartmentId, userId, before, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeletePlanAsync(int departmentId, int lmsTrainingPlanId, string userId, CancellationToken cancellationToken = default)
		{
			var plan = await GetPlanAsync(departmentId, lmsTrainingPlanId);
			if (plan == null)
				return false;
			var before = JsonConvert.SerializeObject(plan);
			plan.IsDeleted = true;
			plan.UpdatedOn = DateTime.UtcNow;
			plan.UpdatedByUserId = userId;
			await _plans.SaveOrUpdateAsync(plan, cancellationToken, true);
			Audit(departmentId, userId, before, JsonConvert.SerializeObject(plan));
			return true;
		}

		public async Task<LmsTrainingPlan> SetStatusAsync(int departmentId, int lmsTrainingPlanId, LmsTrainingPlanStatuses status, string userId, CancellationToken cancellationToken = default)
		{
			var plan = await RequirePlanAsync(departmentId, lmsTrainingPlanId);
			if (!Enum.IsDefined(typeof(LmsTrainingPlanStatuses), status))
				throw new InvalidOperationException("lms_program_status_invalid");
			var before = JsonConvert.SerializeObject(plan);
			plan.Status = (int)status;
			if (status == LmsTrainingPlanStatuses.Adopted)
			{
				plan.AdoptedOn = DateTime.UtcNow;
				plan.AdoptedByUserId = userId;
			}
			plan.UpdatedOn = DateTime.UtcNow;
			plan.UpdatedByUserId = userId;
			var saved = await _plans.SaveOrUpdateAsync(plan, cancellationToken, true);
			Audit(departmentId, userId, before, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<LmsTrainingPlan> CopyPlanAsync(int departmentId, int lmsTrainingPlanId, int year, string userId, CancellationToken cancellationToken = default)
		{
			var source = await RequirePlanAsync(departmentId, lmsTrainingPlanId);
			var copy = await SavePlanAsync(new LmsTrainingPlan
			{
				DepartmentId = departmentId,
				Year = year,
				Name = source.Name,
				ScopeKind = source.ScopeKind,
				ScopeId = source.ScopeId,
				Notes = source.Notes
			}, userId, cancellationToken);

			var shift = year - source.Year;
			foreach (var item in await GetItemsAsync(departmentId, source.LmsTrainingPlanId))
			{
				DateTime? plannedOn = null;
				if (item.PlannedOn.HasValue)
				{
					try { plannedOn = item.PlannedOn.Value.AddYears(shift); }
					catch (ArgumentOutOfRangeException) { plannedOn = null; }
				}

				await _items.SaveOrUpdateAsync(new LmsTrainingPlanItem
				{
					LmsTrainingPlanId = copy.LmsTrainingPlanId,
					DepartmentId = departmentId,
					Month = item.Month,
					CategoryId = item.CategoryId,
					Title = item.Title,
					PlannedHours = item.PlannedHours,
					PlannedSessions = item.PlannedSessions,
					PlannedOn = plannedOn,
					Notes = item.Notes,
					CreatedOn = DateTime.UtcNow,
					CreatedByUserId = userId
				}, cancellationToken, true);
			}

			return copy;
		}

		public async Task<LmsTrainingPlanItem> SaveItemAsync(LmsTrainingPlanItem item, string userId, CancellationToken cancellationToken = default)
		{
			if (item == null || item.DepartmentId <= 0)
				throw new InvalidOperationException("lms_program_plan_not_found");
			var plan = await RequirePlanAsync(item.DepartmentId, item.LmsTrainingPlanId);
			var title = Trim(item.Title, LmsTrainingPlanItem.TitleMaxLength);
			if (string.IsNullOrWhiteSpace(title))
				throw new InvalidOperationException("lms_program_title_required");
			if (item.Month < 1 || item.Month > 12)
				throw new InvalidOperationException("lms_program_month_invalid");
			if (item.PlannedHours <= 0 || item.PlannedHours > LmsTrainingPlanItem.MaxHours)
				throw new InvalidOperationException("lms_program_hours_invalid");
			var sessions = item.PlannedSessions <= 0 ? 1 : item.PlannedSessions;
			if (sessions > LmsTrainingPlanItem.MaxSessions)
				throw new InvalidOperationException("lms_program_sessions_invalid");
			if (item.CategoryId.HasValue && !(await _learningRecords.GetCategoriesAsync(item.DepartmentId) ?? new List<TrainingCategory>())
				    .Any(c => c.TrainingCategoryId == item.CategoryId.Value && !c.IsDeleted))
				throw new InvalidOperationException("lms_program_category_invalid");
			if (item.PlannedOn.HasValue && (item.PlannedOn.Value.Year != plan.Year || item.PlannedOn.Value.Month != item.Month))
				throw new InvalidOperationException("lms_program_date_outside_month");

			LmsTrainingPlanItem stored;
			if (item.LmsTrainingPlanItemId > 0)
			{
				stored = await _items.GetByIdForDepartmentAsync(item.DepartmentId, item.LmsTrainingPlanItemId);
				if (stored == null || stored.LmsTrainingPlanId != plan.LmsTrainingPlanId)
					throw new InvalidOperationException("lms_program_plan_not_found");
				stored.UpdatedOn = DateTime.UtcNow;
			}
			else
			{
				stored = new LmsTrainingPlanItem { DepartmentId = item.DepartmentId, LmsTrainingPlanId = plan.LmsTrainingPlanId, CreatedOn = DateTime.UtcNow, CreatedByUserId = userId };
			}

			stored.Month = item.Month;
			stored.CategoryId = item.CategoryId;
			stored.Title = title;
			stored.PlannedHours = Math.Round(item.PlannedHours, 2, MidpointRounding.AwayFromZero);
			stored.PlannedSessions = sessions;
			stored.PlannedOn = item.PlannedOn?.Date;
			stored.Notes = Trim(item.Notes, LmsTrainingPlanItem.NotesMaxLength);
			var saved = await _items.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(item.DepartmentId, userId, null, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeleteItemAsync(int departmentId, int lmsTrainingPlanItemId, string userId, CancellationToken cancellationToken = default)
		{
			var item = await _items.GetByIdForDepartmentAsync(departmentId, lmsTrainingPlanItemId);
			if (item == null || item.DepartmentId != departmentId || await GetPlanAsync(departmentId, item.LmsTrainingPlanId) == null)
				return false;
			var deleted = await _items.DeleteAsync(item, cancellationToken);
			Audit(departmentId, userId, JsonConvert.SerializeObject(item), null);
			return deleted;
		}

		#endregion Plans and lines

		#region Dashboard

		/// <summary>The active members a plan covers: the department, the group's members, or the shift's personnel.</summary>
		public async Task<HashSet<string>> AudienceAsync(LmsTrainingPlan plan)
		{
			var active = await _departments.GetActiveMemberUserIdsAsync(plan.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			switch ((LmsTrainingPlanScopes)plan.ScopeKind)
			{
				case LmsTrainingPlanScopes.Group when plan.ScopeId.HasValue:
					var members = await _groups.GetAllMembersForGroupAsync(plan.ScopeId.Value) ?? new List<DepartmentGroupMember>();
					return new HashSet<string>(members.Where(m => m != null && m.UserId != null && active.Contains(m.UserId)).Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
				case LmsTrainingPlanScopes.Shift when plan.ScopeId.HasValue:
					var shift = await _shifts.GetShiftByIdAsync(plan.ScopeId.Value);
					if (shift == null || shift.DepartmentId != plan.DepartmentId)
						return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					return new HashSet<string>((shift.Personnel ?? new List<ShiftPerson>()).Where(p => p?.UserId != null && active.Contains(p.UserId)).Select(p => p.UserId),
						StringComparer.OrdinalIgnoreCase);
				default:
					return new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
			}
		}

		private async Task<bool> IsScopeOfDepartmentAsync(int departmentId, LmsTrainingPlanScopes scope, int? scopeId)
		{
			if (!scopeId.HasValue)
				return false;
			if (scope == LmsTrainingPlanScopes.Group)
			{
				var group = await _groups.GetGroupByIdAsync(scopeId.Value, false);
				return group != null && group.DepartmentId == departmentId;
			}
			if (scope == LmsTrainingPlanScopes.Shift)
			{
				var shift = await _shifts.GetShiftByIdAsync(scopeId.Value);
				return shift != null && shift.DepartmentId == departmentId;
			}
			return false;
		}

		public async Task<LmsTrainingPlanDashboard> GetDashboardAsync(int departmentId, int lmsTrainingPlanId, DateTime utcNow)
		{
			var plan = await RequirePlanAsync(departmentId, lmsTrainingPlanId);
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var items = await GetItemsAsync(departmentId, lmsTrainingPlanId);
			var categories = await _learningRecords.GetCategoriesAsync(departmentId, true) ?? new List<TrainingCategory>();
			var audience = await AudienceAsync(plan);
			var asOfLocal = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).TimeConverter(department);

			var dashboard = new LmsTrainingPlanDashboard
			{
				Plan = plan,
				Items = items,
				MemberCount = audience.Count,
				AsOfLocal = asOfLocal,
				AsOfMonth = LmsTrainingPlanMath.AsOfMonth(plan.Year, asOfLocal)
			};

			var planned = LmsTrainingPlanMath.PlannedByCategory(items);
			var actual = await ActualByCategoryAsync(departmentId, plan.Year, department, audience);

			// Rows: every category planned, every category with an annual target, and the uncategorized plan lines.
			var rowCategories = new HashSet<int>(planned.Keys);
			foreach (var category in categories.Where(c => !c.IsDeleted && LmsTrainingPlanMath.AnnualTarget(c).HasValue))
				rowCategories.Add(category.TrainingCategoryId);

			foreach (var key in rowCategories)
			{
				var category = categories.FirstOrDefault(c => c.TrainingCategoryId == key);
				var row = new LmsTrainingPlanCategoryRow
				{
					CategoryId = key == 0 ? (int?)null : key,
					CategoryName = category?.Name,
					AnnualTarget = LmsTrainingPlanMath.AnnualTarget(category),
					PlannedByMonth = planned.TryGetValue(key, out var p) ? p : new decimal[12],
					ActualByMonth = actual.TryGetValue(key, out var a) ? a : new decimal[12]
				};
				LmsTrainingPlanMath.Compute(row, plan.Year, asOfLocal);
				dashboard.Rows.Add(row);
			}

			dashboard.Rows = dashboard.Rows.OrderBy(r => r.CategoryId.HasValue ? 0 : 1)
				.ThenBy(r => categories.FirstOrDefault(c => c.TrainingCategoryId == r.CategoryId)?.SortOrder ?? int.MaxValue)
				.ThenBy(r => r.CategoryName, StringComparer.CurrentCultureIgnoreCase).ToList();
			return dashboard;
		}

		/// <summary>Verified hours per member of the audience, by category and department-local month of the plan year.</summary>
		private async Task<Dictionary<int, decimal[]>> ActualByCategoryAsync(int departmentId, int year, Department department, HashSet<string> audience)
		{
			var result = new Dictionary<int, decimal[]>();
			if (audience.Count == 0)
				return result;

			var fromUtc = LocalToUtc(new DateTime(year, 1, 1), department);
			var toUtc = LocalToUtc(new DateTime(year + 1, 1, 1), department);
			for (var skip = 0; skip < LedgerCap; skip += LedgerPage)
			{
				var page = await _learningRecords.QueryAsync(new LearningRecordQuery
				{
					DepartmentId = departmentId,
					From = fromUtc,
					To = toUtc,
					Statuses = new List<int> { (int)LearningRecordStatuses.Verified },
					Skip = skip,
					Take = LedgerPage
				}) ?? new List<LearningRecord>();

				foreach (var record in page.Where(r => r.DepartmentId == departmentId && r.UserId != null && audience.Contains(r.UserId)))
				{
					var local = DateTime.SpecifyKind(record.CompletedOn, DateTimeKind.Utc).TimeConverter(department);
					if (local.Year != year)
						continue;
					var key = record.CategoryId ?? 0;
					if (!result.TryGetValue(key, out var months))
						result[key] = months = new decimal[12];
					months[local.Month - 1] += record.Hours;
				}

				if (page.Count < LedgerPage)
					break;
			}

			foreach (var months in result.Values)
				for (var i = 0; i < 12; i++)
					months[i] = LmsTrainingPlanMath.Round(months[i] / audience.Count);
			return result;
		}

		private static DateTime LocalToUtc(DateTime local, Department department) =>
			DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);

		#endregion Dashboard

		#region Calendar

		public async Task<LmsTrainingPlanItem> CreateCalendarItemAsync(int departmentId, int lmsTrainingPlanItemId, string userId, CancellationToken cancellationToken = default)
		{
			var item = await _items.GetByIdForDepartmentAsync(departmentId, lmsTrainingPlanItemId);
			if (item == null || item.DepartmentId != departmentId)
				throw new InvalidOperationException("lms_program_plan_not_found");
			var plan = await RequirePlanAsync(departmentId, item.LmsTrainingPlanId);
			if (item.CalendarItemId.HasValue)
			{
				var existing = await _calendar.GetCalendarItemByIdAsync(item.CalendarItemId.Value);
				if (existing != null && existing.DepartmentId == departmentId)
					return item;
			}

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var types = await _calendar.GetAllCalendarItemTypesForDepartmentAsync(departmentId) ?? new List<CalendarItemType>();
			var type = types.FirstOrDefault(t => t.CountsAsTraining && item.CategoryId.HasValue && t.DefaultCategoryId == item.CategoryId)
				?? types.FirstOrDefault(t => t.CountsAsTraining);

			// The planned date, else the first weekday of the month; 09:00 department-local, as long as one session.
			var day = item.PlannedOn?.Date ?? FirstWeekday(plan.Year, item.Month);
			var start = day.AddHours(9);
			var minutes = (double)Math.Clamp(item.PlannedHours, 0.25m, 24m) * 60d;
			var calendarItem = new CalendarItem
			{
				DepartmentId = departmentId,
				Title = item.Title,
				Start = start,
				End = start.AddMinutes(minutes),
				StartTimezone = department?.TimeZone,
				EndTimezone = department?.TimeZone,
				Description = item.Notes,
				ItemType = type?.CalendarItemTypeId ?? 0,
				CreatorUserId = userId,
				Public = true
			};

			var saved = await _calendar.AddNewCalendarItemAsync(calendarItem, department?.TimeZone, cancellationToken);
			if (saved == null || saved.CalendarItemId <= 0)
				throw new InvalidOperationException("lms_program_calendar_failed");

			item.CalendarItemId = saved.CalendarItemId;
			item.UpdatedOn = DateTime.UtcNow;
			var stored = await _items.SaveOrUpdateAsync(item, cancellationToken, true);
			Audit(departmentId, userId, null, JsonConvert.SerializeObject(stored));
			return stored;
		}

		private static DateTime FirstWeekday(int year, int month)
		{
			var day = new DateTime(year, month, 1);
			while (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday)
				day = day.AddDays(1);
			return day;
		}

		#endregion Calendar

		private static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		private void Audit(int departmentId, string userId, string before, string after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.LmsTrainingPlanChanged,
				Before = before,
				After = after,
				Successful = true
			});
		}
	}
}

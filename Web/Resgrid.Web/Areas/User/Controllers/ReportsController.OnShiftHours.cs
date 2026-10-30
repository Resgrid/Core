using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Reporting;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Reports.Shifts;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// On-Shift Hours: every person's time on shift for a period across assigned, signup and static shifts, for the
	/// department, a group or one shift, with each occurrence marked as approved (staffing set on the Shift Staffing page)
	/// or automatic (from the shift's assignments and signups).
	/// </summary>
	public partial class ReportsController
	{
		private const int MaxOnShiftHoursDays = 366;
		private const string ShiftKeyPrefix = "s:";
		private const string WorkshiftKeyPrefix = "w:";

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> OnShiftHoursReportParams()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var now = DateTime.UtcNow.TimeConverter(department);

			var model = new OnShiftHoursReportParams();
			model.Start = new DateTime(now.Year, now.Month, 1);
			model.End = model.Start.AddMonths(1);

			var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
				.Where(x => x != null)
				.OrderBy(x => x.Name)
				.ToList();
			model.Groups = new SelectList(groups, "DepartmentGroupId", "Name");

			var assigned = new SelectListGroup { Name = _onShiftHoursLocalizer["ShiftGroupAssigned"] };
			var signup = new SelectListGroup { Name = _onShiftHoursLocalizer["ShiftGroupSignup"] };
			var staticShifts = new SelectListGroup { Name = _onShiftHoursLocalizer["ShiftGroupStatic"] };

			foreach (var shift in (await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>()).Where(x => x != null).OrderBy(x => x.Name))
			{
				model.Shifts.Add(new SelectListItem
				{
					Value = ShiftKeyPrefix + shift.ShiftId,
					Text = shift.Name,
					Group = shift.AssignmentType == (int)ShiftAssignmentTypes.Signup ? signup : assigned
				});
			}

			foreach (var workshift in (await _workshiftsService.GetAllWorkshiftsByDepartmentAsync(DepartmentId) ?? new List<Workshift>())
				         .Where(x => x != null && x.DeletedOn == null).OrderBy(x => x.Name))
			{
				model.Shifts.Add(new SelectListItem { Value = WorkshiftKeyPrefix + workshift.WorkshiftId, Text = workshift.Name, Group = staticShifts });
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public IActionResult OnShiftHoursReportParams(OnShiftHoursReportParams model)
		{
			var groupId = model.Scope == OnShiftHoursScopes.Group ? model.GroupId : 0;
			var shift = model.Scope == OnShiftHoursScopes.Shift ? model.Shift : null;

			return RedirectToAction("OnShiftHoursReport", new { start = model.Start.ToString("o"), end = model.End.ToString("o"), groupId, shift });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> OnShiftHoursReport(DateTime start, DateTime end, int groupId = 0, string shift = null)
		{
			var model = await OnShiftHoursReportModel(start, end, groupId, shift);

			// A group or shift that isn't one of the department's.
			if (model == null)
				return NotFound();

			return View(model);
		}

		/// <summary>The report's detail, one row per person per shift occurrence, as CSV (payroll / spreadsheet use).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> OnShiftHoursReportCsv(DateTime start, DateTime end, int groupId = 0, string shift = null)
		{
			var model = await OnShiftHoursReportModel(start, end, groupId, shift);

			if (model == null)
				return NotFound();

			string Time(DateTime value) => value.FormatForDepartment(model.Department, true);
			string Hours(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

			var csv = new StringBuilder();
			csv.AppendLine(String.Join(",", new[]
			{
				_onShiftHoursLocalizer["IdHeader"].Value, _onShiftHoursLocalizer["NameHeader"].Value, _onShiftHoursLocalizer["GroupHeader"].Value,
				_onShiftHoursLocalizer["ShiftHeader"].Value, _onShiftHoursLocalizer["TypeHeader"].Value, _onShiftHoursLocalizer["AssignmentHeader"].Value,
				_onShiftHoursLocalizer["StartHeader"].Value, _onShiftHoursLocalizer["EndHeader"].Value, _onShiftHoursLocalizer["HoursHeader"].Value,
				_onShiftHoursLocalizer["StaffingHeader"].Value
			}.Select(CsvCell)));

			foreach (var person in model.People)
			{
				foreach (var entry in person.Entries)
				{
					csv.AppendLine(String.Join(",", new[]
					{
						person.ID, person.Name, person.Group, entry.ShiftName, entry.TypeName, entry.Assignment, Time(entry.Start), Time(entry.End), Hours(entry.Hours),
						SourceText(entry.Source)
					}.Select(CsvCell)));
				}
			}

			return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv", "on-shift-hours.csv");
		}

		/// <summary>
		/// Builds the report for department-local [start, end); null when the group or shift is not the department's. An
		/// end of 23:59:59 counts the whole of that last day.
		/// </summary>
		private async Task<OnShiftHoursView> OnShiftHoursReportModel(DateTime start, DateTime end, int groupId, string shift)
		{
			var model = new OnShiftHoursView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			if (start == default(DateTime) || end == default(DateTime))
			{
				start = new DateTime(model.RunOn.Year, model.RunOn.Month, 1);
				end = start.AddMonths(1);
			}

			if (end < start)
				(start, end) = (end, start);

			if (end.TimeOfDay == new TimeSpan(23, 59, 59))
				end = end.Date.AddDays(1);

			if ((end - start).TotalDays > MaxOnShiftHoursDays)
				end = start.AddDays(MaxOnShiftHoursDays);

			model.Start = start;
			model.End = end;
			model.ScopeName = model.Department?.Name;

			int? shiftId = null;
			string workshiftId = null;

			if (groupId > 0)
			{
				var group = await _departmentGroupsService.GetGroupByIdAsync(groupId, false);

				if (group == null || group.DepartmentId != DepartmentId)
					return null;

				model.Scope = OnShiftHoursScopes.Group;
				model.ScopeName = group.Name;
				model.GroupId = groupId;
			}
			else if (!String.IsNullOrWhiteSpace(shift))
			{
				if (shift.StartsWith(ShiftKeyPrefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(shift.Substring(ShiftKeyPrefix.Length), out var id))
				{
					var selected = await _shiftsService.GetShiftByIdAsync(id);

					if (selected == null || selected.DepartmentId != DepartmentId)
						return null;

					shiftId = selected.ShiftId;
					model.ScopeName = selected.Name;
				}
				else if (shift.StartsWith(WorkshiftKeyPrefix, StringComparison.OrdinalIgnoreCase))
				{
					var selected = await _workshiftsService.GetWorkshiftByIdAsync(shift.Substring(WorkshiftKeyPrefix.Length));

					if (selected == null || selected.DepartmentId != DepartmentId)
						return null;

					workshiftId = selected.WorkshiftId;
					model.ScopeName = selected.Name;
				}
				else
				{
					return null;
				}

				model.Scope = OnShiftHoursScopes.Shift;
				model.Shift = shift;
			}

			var report = await _shiftStaffingService.GetOnShiftHoursAsync(DepartmentId, start, end, groupId > 0 ? groupId : (int?)null, shiftId, workshiftId);

			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
			await ApplyMemberIdentificationNumbersAsync(DepartmentId, profiles.Values);
			var names = (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
				.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.First().Name, StringComparer.OrdinalIgnoreCase);
			var groupNames = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
				.Where(x => x != null)
				.GroupBy(x => x.DepartmentGroupId)
				.ToDictionary(x => x.Key, x => x.First().Name);
			var unitNames = (await _unitsService.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId) ?? new List<Unit>())
				.Where(x => x != null)
				.GroupBy(x => x.UnitId)
				.ToDictionary(x => x.Key, x => x.First().Name);
			var unknown = _onShiftHoursLocalizer["UnknownPerson"].Value;

			string NameFor(string userId) => userId != null && names.TryGetValue(userId, out var name) && !String.IsNullOrWhiteSpace(name) ? name : unknown;

			foreach (var person in report.People)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(person.UserId, UserId, DepartmentId))
					continue;

				profiles.TryGetValue(person.UserId, out var profile);
				var homeGroup = await _departmentGroupsService.GetGroupForUserAsync(person.UserId, DepartmentId);

				model.People.Add(new OnShiftHoursPersonRow
				{
					UserId = person.UserId,
					ID = profile?.IdentificationNumber,
					Name = profile != null ? profile.FullName.AsFirstNameLastName : NameFor(person.UserId),
					Group = homeGroup?.Name,
					ShiftCount = person.ShiftCount,
					ManualShiftCount = person.ManualShiftCount,
					TotalHours = person.TotalHours,
					ManualHours = person.ManualHours,
					AutomaticHours = person.AutomaticHours,
					AssignedHours = person.HoursFor(StaffedShiftTypes.Assigned),
					SignupHours = person.HoursFor(StaffedShiftTypes.Signup),
					StaticHours = person.HoursFor(StaffedShiftTypes.Workshift),
					Entries = person.Entries.Select(x => new OnShiftHoursEntryRow
					{
						ShiftName = x.Staffing.Name,
						ShiftType = x.ShiftType,
						TypeName = TypeText(x.ShiftType),
						Start = x.Start,
						End = x.End,
						Hours = x.Hours,
						IsPartial = x.IsPartial,
						Source = x.Source,
						Assignment = x.UnitId.HasValue
							? (unitNames.TryGetValue(x.UnitId.Value, out var unitName) ? unitName : "")
							: (x.DepartmentGroupId.HasValue && groupNames.TryGetValue(x.DepartmentGroupId.Value, out var groupName) ? groupName : "")
					}).ToList()
				});
			}

			model.People = model.People.OrderByDescending(x => x.TotalHours).ThenBy(x => x.Name).ToList();

			model.Occurrences = report.Occurrences.Select(x => new OnShiftHoursOccurrenceRow
			{
				Day = x.Day,
				Name = x.Name,
				ShiftType = x.ShiftType,
				TypeName = TypeText(x.ShiftType),
				Start = x.Start,
				End = x.End,
				Hours = x.Hours,
				People = x.UserIds().Count,
				Source = x.Source,
				SetBy = x.Source == ShiftStaffingSources.Manual ? NameFor(x.SetByUserId) : null,
				SetOn = x.SetOn.HasValue ? x.SetOn.Value.TimeConverter(model.Department) : (DateTime?)null,
				Added = x.AddedUserIds().Count,
				Removed = x.RemovedUserIds().Count,
				IsOver = x.IsOver,
				ShiftId = x.ShiftId,
				WorkshiftId = x.WorkshiftId
			}).ToList();

			return model;
		}

		private string TypeText(StaffedShiftTypes type)
		{
			switch (type)
			{
				case StaffedShiftTypes.Signup: return _onShiftHoursLocalizer["TypeSignup"].Value;
				case StaffedShiftTypes.Workshift: return _onShiftHoursLocalizer["TypeStatic"].Value;
				default: return _onShiftHoursLocalizer["TypeAssigned"].Value;
			}
		}

		private string SourceText(ShiftStaffingSources source)
		{
			return source == ShiftStaffingSources.Manual ? _onShiftHoursLocalizer["SourceManual"].Value : _onShiftHoursLocalizer["SourceAutomatic"].Value;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Effective staffing for shift days and static shift (workshift) days. A shift day is staffed automatically from its
	/// resolved roster (<see cref="ShiftRosterBuilder"/>) and a static shift day from the active crews of its units, until a
	/// supervisor sets the staffing: the latest ShiftStaffings row for a shift day, or the WorkshiftFills of a static shift
	/// day, then replaces the automatic staffing.
	/// </summary>
	public class ShiftStaffingService : IShiftStaffingService
	{
		// Shifts run up to 72 hours (48/96 rotations with long Hours), so a day that started this long before the window
		// can still overlap it.
		private const int LookBackDays = 3;

		private readonly IShiftsService _shiftsService;
		private readonly IWorkShiftsService _workShiftsService;
		private readonly IShiftStaffingRepository _shiftStaffingRepository;
		private readonly IShiftStaffingPersonRepository _shiftStaffingPersonRepository;
		private readonly IWorkshiftFillsRepository _workshiftFillsRepository;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public ShiftStaffingService(IShiftsService shiftsService, IWorkShiftsService workShiftsService, IShiftStaffingRepository shiftStaffingRepository,
			IShiftStaffingPersonRepository shiftStaffingPersonRepository, IWorkshiftFillsRepository workshiftFillsRepository, IUnitsService unitsService,
			IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService)
		{
			_shiftsService = shiftsService;
			_workShiftsService = workShiftsService;
			_shiftStaffingRepository = shiftStaffingRepository;
			_shiftStaffingPersonRepository = shiftStaffingPersonRepository;
			_workshiftFillsRepository = workshiftFillsRepository;
			_unitsService = unitsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
		}

		#region Reads

		public async Task<List<ShiftDayStaffing>> GetStaffingForRangeAsync(int departmentId, DateTime localStart, DateTime localEnd, int? shiftId = null,
			string workshiftId = null)
		{
			if (localEnd < localStart)
				(localStart, localEnd) = (localEnd, localStart);

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var localNow = department != null ? DateTime.UtcNow.TimeConverter(department) : (DateTime?)null;
			var occurrences = new List<ShiftDayStaffing>();

			var onlyWorkshift = !String.IsNullOrWhiteSpace(workshiftId);
			var onlyShift = shiftId.HasValue && shiftId.Value > 0;

			if (!onlyWorkshift)
			{
				var schedules = await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(departmentId, localStart.Date.AddDays(-LookBackDays), localEnd.Date,
					onlyShift ? shiftId : null) ?? new List<ShiftDaySchedule>();
				var manual = LatestByShiftDay(await _shiftStaffingRepository.GetShiftStaffingsByDepartmentIdAndDateRangeAsync(departmentId,
					localStart.Date.AddDays(-LookBackDays), localEnd.Date.AddDays(1)));

				foreach (var schedule in schedules.Where(x => x?.Shift != null && x.Day != null))
				{
					manual.TryGetValue((schedule.Shift.ShiftId, schedule.Day.Day.Date), out var staffing);
					var occurrence = FromSchedule(schedule, staffing, localNow);

					if (Overlaps(occurrence, localStart, localEnd))
						occurrences.Add(occurrence);
				}
			}

			if (!onlyShift)
			{
				var workshifts = (await _workShiftsService.GetAllWorkshiftsByDepartmentAsync(departmentId) ?? new List<Workshift>())
					.Where(x => x != null && x.DeletedOn == null && (!onlyWorkshift || x.WorkshiftId == workshiftId))
					.ToList();

				var candidates = workshifts
					.Select(x => new
					{
						Workshift = x,
						Days = (x.Days ?? new List<WorkshiftDay>()).Where(d => d != null && Overlaps(WorkshiftDayWindow(x, d, department), localStart, localEnd)).ToList()
					})
					.Where(x => x.Days.Any())
					.ToList();

				if (candidates.Any())
				{
					var lookups = await LoadUnitLookupsAsync(departmentId);

					foreach (var candidate in candidates)
					{
						var full = await _workShiftsService.GetWorkshiftByIdAsync(candidate.Workshift.WorkshiftId) ?? candidate.Workshift;

						foreach (var day in candidate.Days)
							occurrences.Add(FromWorkshiftDay(candidate.Workshift, day, full.Entities, full.Fills, lookups, department, localNow));
					}
				}
			}

			return occurrences.OrderBy(x => x.Start).ThenBy(x => x.Name).ToList();
		}

		public async Task<ShiftDayStaffing> GetShiftDayStaffingAsync(int shiftDayId)
		{
			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule?.Shift == null || schedule.Day == null)
				return null;

			var manual = await GetManualStaffingRecordsAsync(schedule);
			var department = schedule.Shift.Department ?? await _departmentsService.GetDepartmentByIdAsync(schedule.Shift.DepartmentId, false);
			var localNow = department != null ? DateTime.UtcNow.TimeConverter(department) : (DateTime?)null;

			return FromSchedule(schedule, Latest(manual), localNow);
		}

		public async Task<ShiftDayStaffing> GetWorkshiftDayStaffingAsync(string workshiftDayId)
		{
			if (String.IsNullOrWhiteSpace(workshiftDayId))
				return null;

			var day = await _workShiftsService.GetWorkshiftDayByIdAsync(workshiftDayId);

			if (day == null)
				return null;

			var workshift = await _workShiftsService.GetWorkshiftByIdAsync(day.WorkshiftId);

			if (workshift == null || workshift.DeletedOn != null)
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(workshift.DepartmentId, false);
			var localNow = department != null ? DateTime.UtcNow.TimeConverter(department) : (DateTime?)null;
			var lookups = await LoadUnitLookupsAsync(workshift.DepartmentId);

			return FromWorkshiftDay(workshift, day, workshift.Entities, workshift.Fills, lookups, department, localNow);
		}

		public async Task<OnShiftHoursReport> GetOnShiftHoursAsync(int departmentId, DateTime localStart, DateTime localEnd, int? groupId = null,
			int? shiftId = null, string workshiftId = null)
		{
			if (localEnd < localStart)
				(localStart, localEnd) = (localEnd, localStart);

			var occurrences = await GetStaffingForRangeAsync(departmentId, localStart, localEnd, shiftId, workshiftId);
			Func<ShiftDayStaffing, ShiftDayStaffingPerson, bool> include = null;

			if (groupId.HasValue && groupId.Value > 0)
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>();
				var groupIds = GroupAndChildren(groupId.Value, groups);
				var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				foreach (var id in groupIds)
				{
					foreach (var member in await _departmentGroupsService.GetAllMembersForGroupAsync(id) ?? new List<DepartmentGroupMember>())
					{
						if (member != null && !String.IsNullOrWhiteSpace(member.UserId))
							members.Add(member.UserId);
					}
				}

				include = OnShiftHoursCalculator.GroupFilter(groupIds, members);
			}

			return new OnShiftHoursReport
			{
				Start = localStart,
				End = localEnd,
				Occurrences = occurrences.Where(x => include == null || x.Personnel.Any(p => p != null && include(x, p))).ToList(),
				People = OnShiftHoursCalculator.Calculate(occurrences, localStart, localEnd, include)
			};
		}

		#endregion Reads

		#region Shift days

		public async Task<ShiftActionResult<ShiftStaffingSaveResult>> SetShiftDayStaffingAsync(int departmentId, int shiftDayId, List<ShiftDayStaffingPerson> personnel,
			string note, string userId, ShiftManagementScope scope, CancellationToken cancellationToken = default(CancellationToken))
		{
			scope = scope ?? ShiftManagementScope.None();

			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule?.Shift == null || schedule.Day == null)
				return ShiftActionResult<ShiftStaffingSaveResult>.Fail(ShiftActionErrors.NotFound);

			var shift = schedule.Shift;

			if (shift.DepartmentId != departmentId || !scope.CanSuperviseShift(shift))
				return ShiftActionResult<ShiftStaffingSaveResult>.Fail(ShiftActionErrors.NotAllowed);

			var shiftGroupIds = new HashSet<int>((shift.Groups ?? new List<ShiftGroup>()).Where(x => x != null).Select(x => x.DepartmentGroupId));
			var selected = Distinct((personnel ?? new List<ShiftDayStaffingPerson>())
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
				.Select(x => new ShiftDayStaffingPerson { UserId = x.UserId.Trim(), DepartmentGroupId = NormalizeGroupId(x.DepartmentGroupId) }));

			if (selected.Any(x => x.DepartmentGroupId.HasValue && !shiftGroupIds.Contains(x.DepartmentGroupId.Value)))
				return ShiftActionResult<ShiftStaffingSaveResult>.Fail(ShiftActionErrors.InvalidGroup);

			if (selected.Any(x => !scope.CanManageShiftGroup(shift, x.DepartmentGroupId)))
				return ShiftActionResult<ShiftStaffingSaveResult>.Fail(ShiftActionErrors.NotAllowed);

			var memberIds = await GetCurrentMemberIdsAsync(departmentId);
			selected = Distinct(selected
				.Where(x => memberIds.ContainsKey(x.UserId))
				.Select(x => new ShiftDayStaffingPerson { UserId = memberIds[x.UserId], DepartmentGroupId = x.DepartmentGroupId }));

			var department = shift.Department ?? await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var localNow = department != null ? DateTime.UtcNow.TimeConverter(department) : (DateTime?)null;
			var current = FromSchedule(schedule, Latest(await GetManualStaffingRecordsAsync(schedule)), localNow);

			// Slots the caller does not supervise keep whatever staffing they have now.
			var kept = current.Personnel.Where(x => !scope.CanManageShiftGroup(shift, x.DepartmentGroupId));
			var final = Distinct(kept.Concat(selected));
			var result = new ShiftStaffingSaveResult();

			if (!current.IsOver)
			{
				foreach (var person in selected)
				{
					if (schedule.Roster.Any(x => SameUser(x.UserId, person.UserId) && x.DepartmentGroupId == person.DepartmentGroupId && x.IsOnDuty()))
						continue;

					var assigned = await _shiftsService.AssignUserToShiftDayAsync(shiftDayId, person.UserId, person.DepartmentGroupId, userId, cancellationToken);

					if (assigned.Success)
						result.RosterAdded++;
					else
						result.RosterErrors.Add(new KeyValuePair<string, ShiftActionErrors>(person.UserId, assigned.Error));
				}

				// Only people the caller could see on the staffing and left off come off the roster: someone who joined the
				// roster after an earlier manual staffing was set was never shown, so is not taken off.
				var removed = current.Personnel
					.Where(x => scope.CanManageShiftGroup(shift, x.DepartmentGroupId) && !final.Any(f => SameUser(f.UserId, x.UserId)))
					.Select(x => x.UserId)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.Where(x => schedule.Roster.Any(r => SameUser(r.UserId, x) && r.IsOnDuty()))
					.ToList();

				foreach (var removedUserId in removed)
				{
					var removal = await _shiftsService.RemoveUserFromShiftDayAsync(shiftDayId, removedUserId, userId, note, cancellationToken);

					if (removal.Success)
						result.RosterRemoved++;
					else
						result.RosterErrors.Add(new KeyValuePair<string, ShiftActionErrors>(removedUserId, removal.Error));
				}
			}

			var failed = new HashSet<string>(result.RosterErrors.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
			var staffing = new ShiftStaffing
			{
				ShiftId = shift.ShiftId,
				DepartmentId = departmentId,
				ShiftDay = schedule.Day.Day,
				Note = note,
				AddedByUserId = userId,
				AddedOn = DateTime.UtcNow
			};

			staffing = await _shiftStaffingRepository.SaveOrUpdateAsync(staffing, cancellationToken, true);
			staffing.Personnel = new List<ShiftStaffingPerson>();

			foreach (var person in final)
			{
				// Assigned: the person is also on the day's roster (always true for an ended day the shift itself staffed).
				var onRoster = current.IsOver
					? current.AutomaticPersonnel.Any(x => SameUser(x.UserId, person.UserId))
					: !failed.Contains(person.UserId);

				var row = new ShiftStaffingPerson
				{
					ShiftStaffingId = staffing.ShiftStaffingId,
					UserId = person.UserId,
					GroupId = person.DepartmentGroupId,
					Assigned = onRoster
				};

				staffing.Personnel.Add(await _shiftStaffingPersonRepository.SaveOrUpdateAsync(row, cancellationToken, true));
			}

			result.Staffing = staffing;

			return ShiftActionResult<ShiftStaffingSaveResult>.Ok(result);
		}

		public async Task<ShiftActionResult<bool>> ClearShiftDayStaffingAsync(int departmentId, int shiftDayId, ShiftManagementScope scope,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			scope = scope ?? ShiftManagementScope.None();

			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule?.Shift == null || schedule.Day == null)
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotFound);

			// The manual staffing covers every group on the day, so only someone over the whole shift may drop it.
			if (schedule.Shift.DepartmentId != departmentId || !scope.CanManageShift(schedule.Shift))
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotAllowed);

			var records = await GetManualStaffingRecordsAsync(schedule);

			if (!records.Any())
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotFound);

			foreach (var record in records)
			{
				foreach (var person in record.Personnel ?? new List<ShiftStaffingPerson>())
					await _shiftStaffingPersonRepository.DeleteAsync(person, cancellationToken);

				record.Personnel = null;
				await _shiftStaffingRepository.DeleteAsync(record, cancellationToken);
			}

			return ShiftActionResult<bool>.Ok(true);
		}

		#endregion Shift days

		#region Static shift days

		public async Task<ShiftActionResult<int>> SetWorkshiftDayStaffingAsync(int departmentId, string workshiftDayId, List<ShiftDayStaffingPerson> personnel,
			string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var loaded = await LoadWorkshiftDayAsync(departmentId, workshiftDayId);

			if (!loaded.Success)
				return ShiftActionResult<int>.Fail(loaded.Error);

			var (workshift, day) = loaded.Item;
			var entities = (workshift.Entities ?? new List<WorkshiftEntity>()).Where(x => x != null).ToList();
			var entityByUnit = new Dictionary<int, WorkshiftEntity>();

			foreach (var entity in entities)
			{
				if (int.TryParse(entity.BackingId, out var unitId) && !entityByUnit.ContainsKey(unitId))
					entityByUnit.Add(unitId, entity);
			}

			var selected = (personnel ?? new List<ShiftDayStaffingPerson>())
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
				.ToList();

			if (selected.Any(x => !x.UnitId.HasValue || !entityByUnit.ContainsKey(x.UnitId.Value)))
				return ShiftActionResult<int>.Fail(ShiftActionErrors.InvalidRequest);

			var memberIds = await GetCurrentMemberIdsAsync(departmentId);
			var fills = selected
				.Where(x => memberIds.ContainsKey(x.UserId))
				.GroupBy(x => (UserId: memberIds[x.UserId], UnitId: x.UnitId.Value))
				.Select(x => x.Key)
				.ToList();

			// No fills means "automatic" for a static shift day, so an empty staffing can't be stored; clearing is explicit.
			if (!fills.Any())
				return ShiftActionResult<int>.Fail(ShiftActionErrors.NoUsers);

			await DeleteFillsForDayAsync(workshift, day, cancellationToken);

			var now = DateTime.UtcNow;

			foreach (var fill in fills)
			{
				await _workshiftFillsRepository.SaveOrUpdateAsync(new WorkshiftFill
				{
					WorkshiftId = workshift.WorkshiftId,
					WorkshiftDayId = day.WorkshiftDayId,
					WorkshiftEntityId = entityByUnit[fill.UnitId].WorkshiftEntityId,
					FilledById = fill.UserId,
					Approved = true,
					AddedOn = now,
					AddedById = userId
				}, cancellationToken, true);
			}

			return ShiftActionResult<int>.Ok(fills.Count);
		}

		public async Task<ShiftActionResult<bool>> ClearWorkshiftDayStaffingAsync(int departmentId, string workshiftDayId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var loaded = await LoadWorkshiftDayAsync(departmentId, workshiftDayId);

			if (!loaded.Success)
				return ShiftActionResult<bool>.Fail(loaded.Error);

			var (workshift, day) = loaded.Item;

			if (!await DeleteFillsForDayAsync(workshift, day, cancellationToken))
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotFound);

			return ShiftActionResult<bool>.Ok(true);
		}

		#endregion Static shift days

		#region Builders

		/// <summary>A shift day's staffing from its resolved schedule and the manual staffing record in effect (or null).</summary>
		public static ShiftDayStaffing FromSchedule(ShiftDaySchedule schedule, ShiftStaffing manual, DateTime? localNow)
		{
			var shift = schedule.Shift;
			var day = schedule.Day;
			var window = ShiftTimeWindow.GetWindow(day.Day, shift.StartTime, shift.EndTime, shift.Hours);

			var automatic = Distinct((schedule.Roster ?? new List<ShiftDayRosterEntry>())
				.Where(x => x != null && x.IsOnDuty() && !String.IsNullOrWhiteSpace(x.UserId))
				.Select(x => new ShiftDayStaffingPerson { UserId = x.UserId, DepartmentGroupId = x.DepartmentGroupId, RosterSource = x.Source }));

			var staffing = new ShiftDayStaffing
			{
				ShiftType = shift.AssignmentType == (int)ShiftAssignmentTypes.Signup ? StaffedShiftTypes.Signup : StaffedShiftTypes.Assigned,
				ShiftId = shift.ShiftId,
				ShiftDayId = day.ShiftDayId,
				Name = shift.Name,
				Color = shift.Color,
				Day = day.Day.Date,
				Start = window.Start,
				End = window.End,
				AutomaticPersonnel = automatic,
				IsOver = localNow.HasValue && localNow.Value >= window.End,
				IsActive = localNow.HasValue && localNow.Value >= window.Start && localNow.Value < window.End
			};

			if (manual != null)
			{
				staffing.Source = ShiftStaffingSources.Manual;
				staffing.ShiftStaffingId = manual.ShiftStaffingId;
				staffing.SetByUserId = manual.AddedByUserId;
				staffing.SetOn = manual.AddedOn;
				staffing.Note = manual.Note;
				staffing.Personnel = Distinct((manual.Personnel ?? new List<ShiftStaffingPerson>())
					.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
					.Select(x => new ShiftDayStaffingPerson
					{
						UserId = x.UserId,
						DepartmentGroupId = NormalizeGroupId(x.GroupId),
						RosterSource = automatic.FirstOrDefault(a => SameUser(a.UserId, x.UserId))?.RosterSource
					}));
			}
			else
			{
				staffing.Source = ShiftStaffingSources.Automatic;
				staffing.Personnel = automatic.Select(Copy).ToList();
			}

			return staffing;
		}

		/// <summary>
		/// A static shift day's department-local window. Static shifts store Start, End and their days in UTC (the editor and
		/// the shift calendar convert them the same way); without a department they are used as stored.
		/// </summary>
		public static (DateTime Start, DateTime End) WorkshiftDayWindow(Workshift workshift, WorkshiftDay day, Department department)
		{
			var window = ShiftTimeWindow.GetWorkshiftDayWindow(workshift.End, day.Day);

			if (department == null)
				return window;

			return (window.Start.TimeConverter(department), window.End.TimeConverter(department));
		}

		/// <summary>
		/// A static shift day's staffing: its fills when a supervisor set them, otherwise the active crews of its units.
		/// A person's group is the unit's station.
		/// </summary>
		public static ShiftDayStaffing FromWorkshiftDay(Workshift workshift, WorkshiftDay day, IEnumerable<WorkshiftEntity> entities, IEnumerable<WorkshiftFill> fills,
			UnitLookups lookups, Department department, DateTime? localNow)
		{
			var window = WorkshiftDayWindow(workshift, day, department);
			var unitByEntity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			foreach (var entity in (entities ?? Enumerable.Empty<WorkshiftEntity>()).Where(x => x != null && !String.IsNullOrWhiteSpace(x.WorkshiftEntityId)))
			{
				if (int.TryParse(entity.BackingId, out var unitId) && !unitByEntity.ContainsKey(entity.WorkshiftEntityId))
					unitByEntity.Add(entity.WorkshiftEntityId, unitId);
			}

			var automatic = new List<ShiftDayStaffingPerson>();
			foreach (var unitId in unitByEntity.Values.Distinct())
			{
				foreach (var crew in lookups.CrewFor(unitId))
				{
					if (!automatic.Any(x => SameUser(x.UserId, crew) && x.UnitId == unitId))
						automatic.Add(new ShiftDayStaffingPerson { UserId = crew, UnitId = unitId, DepartmentGroupId = lookups.StationFor(unitId) });
				}
			}

			var dayFills = (fills ?? Enumerable.Empty<WorkshiftFill>())
				.Where(x => x != null && x.WorkshiftDayId == day.WorkshiftDayId && !String.IsNullOrWhiteSpace(x.FilledById))
				.ToList();

			var staffing = new ShiftDayStaffing
			{
				ShiftType = StaffedShiftTypes.Workshift,
				WorkshiftId = workshift.WorkshiftId,
				WorkshiftDayId = day.WorkshiftDayId,
				Name = workshift.Name,
				Color = workshift.Color,
				Day = window.Start.Date,
				Start = window.Start,
				End = window.End,
				AutomaticPersonnel = automatic,
				IsOver = localNow.HasValue && localNow.Value >= window.End,
				IsActive = localNow.HasValue && localNow.Value >= window.Start && localNow.Value < window.End
			};

			if (dayFills.Any())
			{
				var latest = dayFills.OrderByDescending(x => x.AddedOn).First();

				staffing.Source = ShiftStaffingSources.Manual;
				staffing.SetByUserId = latest.AddedById;
				staffing.SetOn = latest.AddedOn;
				staffing.Personnel = new List<ShiftDayStaffingPerson>();

				foreach (var fill in dayFills)
				{
					int? unitId = unitByEntity.TryGetValue(fill.WorkshiftEntityId ?? "", out var id) ? id : (int?)null;

					if (!staffing.Personnel.Any(x => SameUser(x.UserId, fill.FilledById) && x.UnitId == unitId))
						staffing.Personnel.Add(new ShiftDayStaffingPerson
						{
							UserId = fill.FilledById,
							UnitId = unitId,
							DepartmentGroupId = unitId.HasValue ? lookups.StationFor(unitId.Value) : null
						});
				}
			}
			else
			{
				staffing.Source = ShiftStaffingSources.Automatic;
				staffing.Personnel = automatic.Select(Copy).ToList();
			}

			return staffing;
		}

		/// <summary>Units' stations and active crews (unit roles people are in right now), for static shift staffing.</summary>
		public class UnitLookups
		{
			public Dictionary<int, int?> Stations { get; set; } = new Dictionary<int, int?>();

			public Dictionary<int, List<string>> Crews { get; set; } = new Dictionary<int, List<string>>();

			public int? StationFor(int unitId) => Stations.TryGetValue(unitId, out var station) ? station : null;

			public IEnumerable<string> CrewFor(int unitId) => Crews.TryGetValue(unitId, out var crew) ? crew : Enumerable.Empty<string>();
		}

		#endregion Builders

		#region Helpers

		private async Task<UnitLookups> LoadUnitLookupsAsync(int departmentId)
		{
			var lookups = new UnitLookups();

			foreach (var unit in await _unitsService.GetUnitsForDepartmentIncludingDeletedAsync(departmentId) ?? new List<Unit>())
			{
				if (unit != null && !lookups.Stations.ContainsKey(unit.UnitId))
					lookups.Stations.Add(unit.UnitId, NormalizeGroupId(unit.StationGroupId));
			}

			foreach (var role in await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitActiveRole>())
			{
				if (role == null || String.IsNullOrWhiteSpace(role.UserId))
					continue;

				if (!lookups.Crews.TryGetValue(role.UnitId, out var crew))
				{
					crew = new List<string>();
					lookups.Crews.Add(role.UnitId, crew);
				}

				if (!crew.Any(x => SameUser(x, role.UserId)))
					crew.Add(role.UserId);
			}

			return lookups;
		}

		private async Task<ShiftActionResult<(Workshift Workshift, WorkshiftDay Day)>> LoadWorkshiftDayAsync(int departmentId, string workshiftDayId)
		{
			if (String.IsNullOrWhiteSpace(workshiftDayId))
				return ShiftActionResult<(Workshift, WorkshiftDay)>.Fail(ShiftActionErrors.NotFound);

			var day = await _workShiftsService.GetWorkshiftDayByIdAsync(workshiftDayId);
			var workshift = day != null ? await _workShiftsService.GetWorkshiftByIdAsync(day.WorkshiftId) : null;

			if (day == null || workshift == null || workshift.DeletedOn != null)
				return ShiftActionResult<(Workshift, WorkshiftDay)>.Fail(ShiftActionErrors.NotFound);

			if (workshift.DepartmentId != departmentId)
				return ShiftActionResult<(Workshift, WorkshiftDay)>.Fail(ShiftActionErrors.NotAllowed);

			return ShiftActionResult<(Workshift, WorkshiftDay)>.Ok((workshift, day));
		}

		private async Task<bool> DeleteFillsForDayAsync(Workshift workshift, WorkshiftDay day, CancellationToken cancellationToken)
		{
			var existing = (workshift.Fills ?? new List<WorkshiftFill>()).Where(x => x != null && x.WorkshiftDayId == day.WorkshiftDayId).ToList();

			foreach (var fill in existing)
				await _workshiftFillsRepository.DeleteAsync(fill, cancellationToken);

			return existing.Any();
		}

		/// <summary>Every manual staffing record saved for the schedule's shift and day, oldest first.</summary>
		private async Task<List<ShiftStaffing>> GetManualStaffingRecordsAsync(ShiftDaySchedule schedule)
		{
			var date = schedule.Day.Day.Date;
			var records = await _shiftStaffingRepository.GetShiftStaffingsByDepartmentIdAndDateRangeAsync(schedule.Shift.DepartmentId, date, date.AddDays(1));

			return (records ?? Enumerable.Empty<ShiftStaffing>())
				.Where(x => x != null && x.ShiftId == schedule.Shift.ShiftId && x.ShiftDay.Date == date)
				.OrderBy(x => x.AddedOn).ThenBy(x => x.ShiftStaffingId)
				.ToList();
		}

		/// <summary>Current members' user ids, keyed case-insensitively to the id as stored.</summary>
		private async Task<Dictionary<string, string>> GetCurrentMemberIdsAsync(int departmentId)
		{
			var members = await _departmentsService.GetAllMembersForDepartmentAsync(departmentId) ?? new List<DepartmentMember>();

			return members
				.Where(x => DepartmentMemberStateHelper.IsCurrentMember(x, departmentId))
				.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.First().UserId, StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>The latest manual staffing per (shift, day): saving again keeps the earlier rows as history.</summary>
		private static Dictionary<(int ShiftId, DateTime Day), ShiftStaffing> LatestByShiftDay(IEnumerable<ShiftStaffing> records)
		{
			return (records ?? Enumerable.Empty<ShiftStaffing>())
				.Where(x => x != null)
				.GroupBy(x => (x.ShiftId, x.ShiftDay.Date))
				.ToDictionary(x => x.Key, x => Latest(x));
		}

		private static ShiftStaffing Latest(IEnumerable<ShiftStaffing> records)
		{
			return (records ?? Enumerable.Empty<ShiftStaffing>())
				.Where(x => x != null)
				.OrderByDescending(x => x.AddedOn).ThenByDescending(x => x.ShiftStaffingId)
				.FirstOrDefault();
		}

		private static HashSet<int> GroupAndChildren(int groupId, List<DepartmentGroup> groups)
		{
			var ids = new HashSet<int> { groupId };
			var added = true;

			while (added)
			{
				added = false;

				foreach (var group in groups.Where(x => x != null && x.ParentDepartmentGroupId.HasValue && ids.Contains(x.ParentDepartmentGroupId.Value)))
				{
					if (ids.Add(group.DepartmentGroupId))
						added = true;
				}
			}

			return ids;
		}

		private static bool Overlaps(ShiftDayStaffing occurrence, DateTime localStart, DateTime localEnd)
		{
			return Overlaps((occurrence.Start, occurrence.End), localStart, localEnd);
		}

		private static bool Overlaps((DateTime Start, DateTime End) window, DateTime localStart, DateTime localEnd)
		{
			return window.Start < localEnd && window.End > localStart;
		}

		private static List<ShiftDayStaffingPerson> Distinct(IEnumerable<ShiftDayStaffingPerson> people)
		{
			var result = new List<ShiftDayStaffingPerson>();

			foreach (var person in people ?? Enumerable.Empty<ShiftDayStaffingPerson>())
			{
				if (!result.Any(x => SameUser(x.UserId, person.UserId) && x.DepartmentGroupId == person.DepartmentGroupId && x.UnitId == person.UnitId))
					result.Add(person);
			}

			return result;
		}

		private static ShiftDayStaffingPerson Copy(ShiftDayStaffingPerson person)
		{
			return new ShiftDayStaffingPerson
			{
				UserId = person.UserId,
				DepartmentGroupId = person.DepartmentGroupId,
				UnitId = person.UnitId,
				RosterSource = person.RosterSource
			};
		}

		private static int? NormalizeGroupId(int? departmentGroupId)
		{
			return departmentGroupId.HasValue && departmentGroupId.Value > 0 ? departmentGroupId : null;
		}

		private static bool SameUser(string a, string b)
		{
			return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}

		#endregion Helpers
	}
}

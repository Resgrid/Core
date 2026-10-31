using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Reporting;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Who is staffing each shift occurrence across every shift type (assigned, signup and static shifts). Staffing is
	/// automatic, worked out from the shift (standing roster, signups, single-day edits, trades, or a static shift's unit
	/// crews), until a supervisor sets it on the Shift Staffing page; that manual (approved) staffing then replaces the
	/// automatic one for that occurrence until it is cleared.
	/// </summary>
	public interface IShiftStaffingService
	{
		/// <summary>
		/// Every shift day and static shift day in the department whose time overlaps [localStart, localEnd) (department
		/// local), with its effective staffing. <paramref name="shiftId"/> limits it to one shift and
		/// <paramref name="workshiftId"/> to one static shift; with neither every shift type is included, unless
		/// <paramref name="workshiftsOnly"/> asks for static shift days alone.
		/// </summary>
		Task<List<ShiftDayStaffing>> GetStaffingForRangeAsync(int departmentId, DateTime localStart, DateTime localEnd, int? shiftId = null,
			string workshiftId = null, bool workshiftsOnly = false);

		/// <summary>The staffing of one shift day, or null for an unknown day.</summary>
		Task<ShiftDayStaffing> GetShiftDayStaffingAsync(int shiftDayId);

		/// <summary>The staffing of one static shift day, or null for an unknown or deleted one.</summary>
		Task<ShiftDayStaffing> GetWorkshiftDayStaffingAsync(string workshiftDayId);

		/// <summary>
		/// A supervisor sets (approves) a shift day's staffing. Only the slots <paramref name="scope"/> covers are replaced;
		/// the rest keep their current staffing. Until the day has ended the day's roster is brought in line too (people
		/// added are assigned to the day, people the supervisor took off are removed from it); a roster change that cannot
		/// be made is reported but does not stop the staffing being saved.
		/// </summary>
		Task<ShiftActionResult<ShiftStaffingSaveResult>> SetShiftDayStaffingAsync(int departmentId, int shiftDayId, List<ShiftDayStaffingPerson> personnel,
			string note, string userId, ShiftManagementScope scope, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Removes the manual staffing of a shift day so it is automatic again. Needs the whole shift in scope. The roster
		/// is left as it is.
		/// </summary>
		Task<ShiftActionResult<bool>> ClearShiftDayStaffingAsync(int departmentId, int shiftDayId, ShiftManagementScope scope,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets (approves) who crews each unit on a static shift day. Every person needs one of the static shift's units.
		/// </summary>
		Task<ShiftActionResult<int>> SetWorkshiftDayStaffingAsync(int departmentId, string workshiftDayId, List<ShiftDayStaffingPerson> personnel,
			string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Removes the manual staffing of a static shift day so it is automatic (the unit crews) again.</summary>
		Task<ShiftActionResult<bool>> ClearWorkshiftDayStaffingAsync(int departmentId, string workshiftDayId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// On-shift hours per person for [localStart, localEnd) (department local) across every shift type, for the
		/// whole department, one group (with its child groups) or one shift / static shift.
		/// </summary>
		Task<OnShiftHoursReport> GetOnShiftHoursAsync(int departmentId, DateTime localStart, DateTime localEnd, int? groupId = null,
			int? shiftId = null, string workshiftId = null);
	}
}

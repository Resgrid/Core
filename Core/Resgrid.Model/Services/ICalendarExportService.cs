using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Generates iCal (RFC 5545) formatted calendar export data from Resgrid CalendarItems.
	/// Each materialized occurrence is emitted as its own VEVENT — no RRULE is used since
	/// the system pre-expands recurrences into individual database rows.
	/// </summary>
	public interface ICalendarExportService
	{
		/// <summary>
		/// Generates a complete iCal (.ics) file containing a single VEVENT for the specified
		/// calendar item.
		/// </summary>
		/// <param name="calendarItemId">The ID of the calendar item to export.</param>
		/// <returns>RFC 5545 iCal string, or null if the item does not exist.</returns>
		Task<string> GenerateICalForItemAsync(int calendarItemId);

		/// <summary>
		/// Generates a complete iCal (.ics) file containing all calendar items for the
		/// specified department. Each item becomes its own VEVENT inside one VCALENDAR.
		/// </summary>
		/// <param name="departmentId">The department whose calendar items should be exported.</param>
		/// <returns>RFC 5545 iCal string.</returns>
		Task<string> GenerateICalForDepartmentAsync(int departmentId);
	}
}


using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICalendarItemAttendeeRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CalendarItemAttendee}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CalendarItemAttendee}" />
	public interface ICalendarItemAttendeeRepository: IRepository<CalendarItemAttendee>
	{
		Task<CalendarItemAttendee> GetCalendarItemAttendeeByUserAsync(int calendarEventItemId, string userId);
	}
}

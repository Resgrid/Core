using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICalendarItemsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CalendarItem}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CalendarItem}" />
	public interface ICalendarItemsRepository: IRepository<CalendarItem>
	{
		/// <summary>
		/// Gets the calendar items by recurrence identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CalendarItem&gt;&gt;.</returns>
		Task<IEnumerable<CalendarItem>> GetCalendarItemsByRecurrenceIdAsync(int id);

		/// <summary>
		/// Deletes the calendar item and recurrences asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteCalendarItemAndRecurrencesAsync(int id, CancellationToken cancellationToken);

		/// <summary>
		/// Gets all calendar items to notify asynchronous.
		/// </summary>
		/// <param name="startDate">The start date.</param>
		/// <returns>Task&lt;IEnumerable&lt;CalendarItem&gt;&gt;.</returns>
		Task<IEnumerable<CalendarItem>> GetAllCalendarItemsToNotifyAsync(DateTime startDate);

		/// <summary>
		/// Gets the calendar item by identifier asynchronous.
		/// </summary>
		/// <param name="calendarItemId">The calendar item identifier.</param>
		/// <returns>Task&lt;CalendarItem&gt;.</returns>
		Task<CalendarItem> GetCalendarItemByIdAsync(int calendarItemId);

		Task<IEnumerable<CalendarItem>> GetAllCalendarItemsByDepartmentIdAsync(int departmentId);
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Awareness;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// calendar.agenda (plan §7.6): GetUpcomingCalendarItemsAsync (the Calendar page's upcoming list, 14 days) cut to the
	/// widget's window, with the viewer's attendee row and check-in. Titles and locations are plain; the description
	/// (ADP) is never read into the DTO. Three reads: items (attendees joined), types, the viewer's check-ins.
	/// </summary>
	[DashboardWidget("calendar.agenda")]
	public class CalendarAgendaWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICalendarService _calendarService;

		public CalendarAgendaWidgetProvider(ICalendarService calendarService)
		{
			_calendarService = calendarService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var now = context.NowUtc;
			var days = context.GetInt("days", 7);
			var windowEnd = now.AddDays(days);

			var data = new CalendarAgendaWidgetData { Days = days, TimeZoneId = ClockWidgetProvider.ToIana(context.Viewer?.TimeZoneId) };

			var items = (await _calendarService.GetUpcomingCalendarItemsAsync(departmentId, now) ?? new List<CalendarItem>())
				.Where(i => i != null && i.DepartmentId == departmentId && i.Start < windowEnd && i.End >= now)
				.ToList();
			data.TotalCount = items.Count;

			if (items.Count == 0)
				return data;

			var types = (await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(departmentId) ?? new List<CalendarItemType>())
				.Where(t => t != null && t.DepartmentId == departmentId)
				.GroupBy(t => t.CalendarItemTypeId)
				.ToDictionary(g => g.Key, g => g.First());

			// The type filter is a list of posted ids: only the department's own types count; none left means no filter.
			var typeFilter = new HashSet<int>(context.GetStrings("types")
				.Select(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1)
				.Where(types.ContainsKey));

			var shown = items
				.Where(i => typeFilter.Count == 0 || typeFilter.Contains(i.ItemType))
				.OrderBy(i => i.Start)
				.ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
				.Take(context.GetInt("maxItems", 10))
				.ToList();

			if (shown.Count == 0)
				return data;

			var checkIns = (await _calendarService.GetCheckInsByUserDateRangeAsync(context.UserId, departmentId, shown.Min(i => i.Start).AddDays(-1), windowEnd)
					?? new List<CalendarItemCheckIn>())
				.Where(c => c != null && c.DepartmentId == departmentId && string.Equals(c.UserId, context.UserId, StringComparison.OrdinalIgnoreCase))
				.GroupBy(c => c.CalendarItemId)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CheckInTime).First());

			foreach (var item in shown)
			{
				types.TryGetValue(item.ItemType, out var type);
				checkIns.TryGetValue(item.CalendarItemId, out var checkIn);
				var attendee = item.Attendees?.FirstOrDefault(a => a != null && string.Equals(a.UserId, context.UserId, StringComparison.OrdinalIgnoreCase));

				data.Items.Add(new CalendarAgendaItem
				{
					CalendarItemId = item.CalendarItemId,
					Title = item.Title,
					Location = item.Location,
					StartUtc = item.Start,
					EndUtc = item.End,
					IsAllDay = item.IsAllDay,
					TypeId = type != null ? item.ItemType : 0,
					TypeName = type?.Name,
					TypeColor = type?.Color,
					SignupType = item.SignupType,
					CheckInType = item.CheckInType,
					MyAttendeeType = attendee?.AttendeeType,
					CheckedIn = checkIn != null,
					CheckedOut = checkIn?.CheckOutTime != null,
					Url = "/User/Calendar/View?calendarItemId=" + item.CalendarItemId.ToString(CultureInfo.InvariantCulture)
				});
			}

			return data;
		}
	}
}

using System;
using Resgrid.Framework;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard.Widgets
{
	/// <summary>The department's local date, as the module pages compute "today" (DepartmentTime) for their defaults.</summary>
	internal static class DashboardDepartmentClock
	{
		public static DateTime Today(DashboardWidgetContext context)
		{
			var zone = context.Viewer?.TimeZoneId;
			if (string.IsNullOrWhiteSpace(zone))
				return context.NowUtc.Date;

			try
			{
				return DateTimeHelpers.GetLocalDateTime(context.NowUtc, zone).Date;
			}
			catch (Exception)
			{
				return context.NowUtc.Date;
			}
		}
	}
}

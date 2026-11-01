using System;
using System.Threading;
using System.Threading.Tasks;
using NodaTime;
using Resgrid.Framework;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Services;
using TimeZoneConverter;

namespace Resgrid.Services.Dashboard.Widgets.Awareness
{
	/// <summary>general.clock (plan §7.4): the client draws the clocks; this only names the department's IANA time zone.</summary>
	[DashboardWidget("general.clock")]
	public class ClockWidgetProvider : IDashboardWidgetDataProvider
	{
		public const string UtcZone = "Etc/UTC";

		private readonly IDepartmentsService _departmentsService;

		public ClockWidgetProvider(IDepartmentsService departmentsService)
		{
			_departmentsService = departmentsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			// The web tier already put the department's zone on the viewer; read the department only when it did not.
			var stored = context.Viewer?.TimeZoneId;
			if (string.IsNullOrWhiteSpace(stored))
				stored = (await _departmentsService.GetDepartmentByIdAsync(context.DepartmentId))?.TimeZone;

			return new ClockWidgetData { TimeZoneId = ToIana(stored), DepartmentTimeZone = stored };
		}

		/// <summary>
		/// A Windows id (or one of the legacy short names) converts to its IANA id; an IANA id passes through; anything else is
		/// UTC. Resolved through NodaTime's embedded tzdb and TimeZoneConverter's mapping rather than TimeZoneInfo, which cannot
		/// map zones in the hardened (globalization-invariant, no ICU) container.
		/// </summary>
		public static string ToIana(string timeZone)
		{
			if (string.IsNullOrWhiteSpace(timeZone))
				return UtcZone;

			timeZone = DateTimeHelpers.ConvertTimeZoneString(timeZone.Trim());
			if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone) != null)
				return timeZone;

			try
			{
				var iana = TZConvert.WindowsToIana(timeZone);
				if (!string.IsNullOrWhiteSpace(iana) && DateTimeZoneProviders.Tzdb.GetZoneOrNull(iana) != null)
					return iana;
			}
			catch (Exception)
			{
				// An unknown id is not a reason to fail the widget.
			}

			return UtcZone;
		}
	}
}

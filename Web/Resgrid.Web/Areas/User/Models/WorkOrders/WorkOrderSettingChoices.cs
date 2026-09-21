using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Web.Areas.User.Models.WorkOrders
{
    public static class WorkOrderSettingChoices
    {
        public static IEnumerable<TimeZoneInfo> TimeZones(string selected)
        {
            var zones = TimeZoneInfo.GetSystemTimeZones().ToList();
            // Stored schedules may use the other OS's zone identifiers. Keep their valid selection.
            if (!string.IsNullOrWhiteSpace(selected) && zones.All(z => z.Id != selected) && TimeZoneInfo.TryFindSystemTimeZoneById(selected, out var stored))
                zones.Add(stored);
            return zones.OrderBy(z => z.BaseUtcOffset).ThenBy(z => z.DisplayName);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// certifications.mine (plan §7.7): the viewer's own certifications in this department (the profile page lists only this
	/// department's records) that are expired or expire within the department's horizon (its longest notification lead, 60 days
	/// by default, as the Certifications dashboard). Reads without file bytes; names, statuses and dates only.
	/// </summary>
	[DashboardWidget("certifications.mine")]
	public class CertificationsMineWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICertificationService _certificationService;

		public CertificationsMineWidgetProvider(ICertificationService certificationService)
		{
			_certificationService = certificationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var records = (await _certificationService.GetCertificationsForDepartmentAsync(context.DepartmentId, new[] { context.UserId }) ?? new List<PersonnelCertification>())
				.Where(c => c != null && c.DepartmentId == context.DepartmentId && !c.IsDeleted && string.Equals(c.UserId, context.UserId, StringComparison.OrdinalIgnoreCase))
				.ToList();

			var settings = await _certificationService.GetCertificationSettingsAsync(context.DepartmentId);
			var leadDays = settings?.GetNotifyLeadDays() ?? Array.Empty<int>();
			var horizon = leadDays.Count > 0 ? leadDays.Max() : 60;
			var today = context.NowUtc.Date;

			var items = records.Select(c =>
				{
					var days = c.ExpiresOn.HasValue ? (int?)(c.ExpiresOn.Value.Date - today).TotalDays : null;
					var expired = c.Status == (int)PersonnelCertificationStatuses.Expired || days < 0;
					var expiring = !expired && c.Status == (int)PersonnelCertificationStatuses.Active && days.HasValue && days <= horizon;
					return (Record: c, Days: days, Expired: expired, Expiring: expiring);
				})
				.Where(x => x.Expired || x.Expiring)
				.ToList();

			return new CertificationsMineWidgetData
			{
				HorizonDays = horizon,
				Total = records.Count,
				Expired = items.Count(x => x.Expired),
				Expiring = items.Count(x => x.Expiring),
				Items = items.OrderBy(x => x.Expired ? 0 : 1).ThenBy(x => x.Days ?? int.MaxValue).Take(maxRows).Select(x => new CertificationsMineItem
				{
					CertificationId = x.Record.PersonnelCertificationId,
					Name = ReadinessWidgetSupport.SafeText(x.Record.Name) ?? ReadinessWidgetSupport.SafeText(x.Record.Type),
					Status = x.Record.Status,
					ExpiresOn = x.Record.ExpiresOn,
					DaysUntilExpiry = x.Days,
					Tone = x.Expired ? "danger" : "warning"
				}).ToList()
			};
		}
	}
}

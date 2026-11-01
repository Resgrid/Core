using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// certifications.expiry (plan §7.7): ICertificationService.GetExpiryDashboardAsync — the Certifications page's department
	/// matrix (deleted, disabled and hidden members already excluded by the service) — its four totals and the most urgent cells.
	/// Names, types, statuses and dates only: certificate numbers and documents are protected and never read here.
	/// </summary>
	[DashboardWidget("certifications.expiry")]
	public class CertificationsExpiryWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICertificationService _certificationService;

		public CertificationsExpiryWidgetProvider(ICertificationService certificationService)
		{
			_certificationService = certificationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var dashboard = await _certificationService.GetExpiryDashboardAsync(context.DepartmentId) ?? new CertificationExpiryDashboard();
			var horizon = dashboard.Horizon;

			var cells = (dashboard.PersonCells ?? new List<CertificationDashboardCell>()).Select(c => (Scope: "person", Cell: c))
				.Concat((dashboard.UnitCells ?? new List<CertificationDashboardCell>()).Select(c => (Scope: "unit", Cell: c)))
				.Where(x => x.Cell != null && (IsExpired(x.Cell) || IsExpiring(x.Cell, horizon)))
				.OrderBy(x => IsExpired(x.Cell) ? 0 : 1).ThenBy(x => x.Cell.DaysUntilExpiry ?? int.MaxValue).ThenBy(x => x.Cell.SubjectName, StringComparer.OrdinalIgnoreCase)
				.Take(maxRows)
				.Select(x => new CertificationsExpiryItem
				{
					Scope = x.Scope,
					SubjectId = x.Cell.SubjectId,
					SubjectName = ReadinessWidgetSupport.SafeText(x.Cell.SubjectName),
					TypeCode = x.Cell.TypeCode,
					TypeName = ReadinessWidgetSupport.SafeText(x.Cell.TypeName),
					Status = x.Cell.Status,
					ExpiresOn = x.Cell.ExpiresOn,
					DaysUntilExpiry = x.Cell.DaysUntilExpiry,
					Tone = IsExpired(x.Cell) ? "danger" : "warning"
				}).ToList();

			return new CertificationsExpiryWidgetData
			{
				HorizonDays = horizon,
				Expired = dashboard.ExpiredCount,
				Expiring = dashboard.ExpiringCount,
				Suspended = dashboard.SuspendedCount,
				PendingVerification = dashboard.PendingVerificationCount,
				Items = cells
			};
		}

		// The same tests CertificationExpiryDashboard.RecountTotals applies.
		private static bool IsExpired(CertificationDashboardCell cell) =>
			cell.Status == (int)PersonnelCertificationStatuses.Expired || cell.DaysUntilExpiry.HasValue && cell.DaysUntilExpiry < 0;

		private static bool IsExpiring(CertificationDashboardCell cell, int horizon) =>
			cell.Status == (int)PersonnelCertificationStatuses.Active && cell.DaysUntilExpiry.HasValue && cell.DaysUntilExpiry >= 0 && cell.DaysUntilExpiry <= horizon;
	}
}

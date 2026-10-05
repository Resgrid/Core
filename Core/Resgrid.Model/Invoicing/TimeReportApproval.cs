using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// Separation of duties for daily time reports. A report is the approver's own when they submitted it or when it
	/// carries their time: an individual report for their roster seat, or any entry for that seat on a crew or
	/// deployment-wide report. Approving it would let one person certify their own billable hours, so
	/// TimeTrackingService refuses it (timereports_self_approval) and the Time Report page hides the Approve button.
	/// Voiding stays open: it removes hours rather than certifying them.
	/// </summary>
	public static class TimeReportApproval
	{
		public const string SelfApprovalRefused = "timereports_self_approval";

		/// <param name="report">The report, with <see cref="DeploymentTimeReport.Entries"/> populated.</param>
		/// <param name="roster">The deployment's roster rows, removed seats included (a removed seat's hours still count).</param>
		/// <param name="userId">The user who would approve.</param>
		public static bool IsOwnReport(DeploymentTimeReport report, IEnumerable<DeploymentPersonnel> roster, string userId)
		{
			if (report == null || string.IsNullOrWhiteSpace(userId))
				return false;

			if (string.Equals(report.SubmittedByUserId, userId, StringComparison.OrdinalIgnoreCase))
				return true;

			var seats = new HashSet<string>((roster ?? Enumerable.Empty<DeploymentPersonnel>())
				.Where(p => p != null && !string.IsNullOrWhiteSpace(p.DeploymentPersonnelId) && string.Equals(p.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.Select(p => p.DeploymentPersonnelId), StringComparer.OrdinalIgnoreCase);
			if (seats.Count == 0)
				return false;

			if (!string.IsNullOrWhiteSpace(report.DeploymentPersonnelId) && seats.Contains(report.DeploymentPersonnelId))
				return true;

			return (report.Entries ?? new List<DeploymentTimeEntry>())
				.Any(e => e != null && !string.IsNullOrWhiteSpace(e.DeploymentPersonnelId) && seats.Contains(e.DeploymentPersonnelId));
		}
	}
}

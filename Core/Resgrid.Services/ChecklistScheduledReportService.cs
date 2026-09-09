using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed class ChecklistScheduledReportService : IChecklistScheduledReportService
	{
		private readonly IScheduledTasksService _tasks;
		private readonly IChecklistAuthorizationService _authorization;
		private readonly IReadinessAccessService _access;
		private readonly IUsersService _users;
		private readonly IUserProfileService _profiles;
		private readonly IPdfProvider _pdf;
		public ChecklistScheduledReportService(IScheduledTasksService tasks, IChecklistAuthorizationService authorization,
			IReadinessAccessService access, IUsersService users, IUserProfileService profiles, IPdfProvider pdf)
		{ _tasks = tasks; _authorization = authorization; _access = access; _users = users; _profiles = profiles; _pdf = pdf; }
		public async Task<EmailNotification> BuildAsync(ScheduledTask queuedTask)
		{
			var task = await _tasks.GetScheduledTaskByIdAsync(queuedTask.ScheduledTaskId);
			if (task == null || !task.Active || task.DepartmentId != queuedTask.DepartmentId || task.UserId != queuedTask.UserId || task.Data != queuedTask.Data
				|| task.TaskType != (int)TaskTypes.ReportDelivery || task.Data != "4" && task.Data != "5") throw new ChecklistException(403, "The request could not be completed.");
			var actor = new ChecklistActor { DepartmentId = task.DepartmentId, UserId = task.UserId };
			await _authorization.RequireMemberAsync(actor);
			if (!await _access.CanUseChecklistsAsync(task.DepartmentId)) throw new ChecklistException(404, "Checklists are disabled for this department.");
			var user = _users.GetUserById(task.UserId, true);
			if (string.IsNullOrWhiteSpace(user?.Email)) throw new ChecklistException(403, "The request could not be completed.");
			var language = (await _profiles.GetProfileByUserIdAsync(task.UserId))?.Language;
			var original = CultureInfo.CurrentCulture; var originalUi = CultureInfo.CurrentUICulture;
			try
			{
				var culture = CultureInfo.GetCultureInfo(Resgrid.Localization.SupportedLocales.GetSupportedCultures().Contains(language) ? language : "en");
				CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture;
				// Unattended delivery carries only a static notice, never results, identities or outcomes.
				// The report is opened through the authenticated link using current permissions and ADP.
				var bytes = _pdf.ConvertHtmlToPdf(ChecklistReportDocuments.Locked());
				if (bytes == null || bytes.Length < 5 || System.Text.Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") throw new InvalidOperationException("Checklist PDF generation failed.");
				await _authorization.RequireMemberAsync(actor);
				if (!await _access.CanUseChecklistsAsync(task.DepartmentId)) throw new ChecklistException(404, "Checklists are disabled for this department.");
				return new EmailNotification { To = user.Email, Subject = ChecklistReportDocuments.Text(task.Data == "5" ? "ChecklistMissedReport" : "ChecklistComplianceReport"), AttachmentName = "checklists-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".pdf", AttachmentData = bytes };
			}
			finally { CultureInfo.CurrentCulture = original; CultureInfo.CurrentUICulture = originalUi; }
		}
	}
}

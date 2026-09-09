using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Checklists;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	public partial class ChecklistsController
	{
		[HttpGet, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> Reminders() => View("Reminders", await _checklists.ReminderSettingsAsync(Actor));
		[HttpPost, ValidateAntiForgeryToken, Authorize(Policy = ResgridResources.Checklist_Update)]
		public async Task<IActionResult> SaveReminders(ChecklistReminderSettingsInput input)
		{
			if (!ModelState.IsValid) throw new ChecklistException(400, "ReminderValidation");
			await _checklists.SaveReminderSettingsAsync(Actor, input);
			return RedirectToAction("Reminders");
		}
	}
}

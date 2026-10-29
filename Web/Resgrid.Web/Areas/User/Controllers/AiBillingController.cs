using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Enhanced AI add-on purchase and renewal page (enhanced-ai-addon-plan.md). The shared flow lives in
	/// <see cref="AddonBillingControllerBase"/>; this adds the AI budget, model and free-allowance notes.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class AiBillingController : AddonBillingControllerBase
	{
		private readonly IAiBillingService _billing;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.EnhancedAi.EnhancedAi> _strings;

		public AiBillingController(IAiBillingService billing, IDepartmentsService departments, Model.Services.IAuthorizationService authorization,
			IFeatureToggleService flags, IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonStrings,
			IStringLocalizer<Resgrid.Localization.Areas.User.EnhancedAi.EnhancedAi> strings)
			: base(departments, authorization, flags, addonStrings)
		{
			_billing = billing;
			_strings = strings;
		}

		protected override PlanAddonTypes AddonType => PlanAddonTypes.EnhancedAi;

		protected override async Task<AddonBillingStatusView> GetStatusAsync() => AddonBillingStatusView.From(await _billing.GetAsync(DepartmentId));

		protected override async Task<AddonCheckoutStart> BeginCheckoutAsync()
		{
			var checkout = await _billing.BeginCheckoutAsync(DepartmentId);
			return checkout == null ? null : new AddonCheckoutStart(checkout.Provider, checkout.Url, checkout.TransactionId);
		}

		protected override Task<bool> CancelRenewalAsync() => _billing.CancelRenewalAsync(DepartmentId);

		protected override Task DecorateAsync(AddonBillingView view)
		{
			view.Notes.Add(_strings["BudgetNote"]);
			view.Notes.Add(_strings["ModelsNote"] + " " + _strings["DataNote"]);
			view.Notes.Add(_strings["ComingLater"]);
			if (AiAddonConfig.AdminAssistFreeEnabled)
				view.SideNotes.Add(new AddonSideNote(_strings["FreeAllowance"], string.Format(_strings["FreeAllowanceText"].Value,
					AiAddonConfig.AdminAssistFreeStarterQuestions, AiAddonConfig.AdminAssistFreeStarterDays, AiAddonConfig.AdminAssistFreeMonthlyQuestions)));
			view.Links.Add(new AddonLink(_strings["FeatureOwnProvider"], Url.Action("Index", "ChatbotSettings", new { Area = "User" })));
			return Task.CompletedTask;
		}
	}
}

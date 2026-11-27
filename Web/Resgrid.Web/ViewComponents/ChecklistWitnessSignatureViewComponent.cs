using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Signatures;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// The checklist witness signature on a completion (e-Signature S3, free with Checklists): for a form that requires it, the
	/// shared signature panel at L2 — the statement, the completion's content code and the attestation box — and the witness
	/// signatures, each checked against the completion's operational content as it is now. Renders nothing for other forms.
	/// </summary>
	public sealed class ChecklistWitnessSignatureViewComponent(IChecklistWitnessSignatureService witness, IProtectedGrantContext grant,
		IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> text) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(string completionId)
		{
			ChecklistWitnessSignatureView view;
			try
			{
				view = await witness.GetAsync(new ChecklistActor
				{
					DepartmentId = ClaimsAuthorizationHelper.GetDepartmentId(),
					UserId = ClaimsAuthorizationHelper.GetUserId(),
					GrantToken = grant.GrantToken
				}, completionId, CultureInfo.CurrentUICulture.Name);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				// A locked or unavailable run: the completion page shows its own state; the witness panel simply stays away.
				Logging.LogException(ex, "The checklist witness signature panel could not be loaded.");
				return Content(string.Empty);
			}

			if (view == null || !view.Required || view.ArtifactSha256 == null)
				return Content(string.Empty);

			var model = new ChecklistWitnessPanelModel
			{
				View = view,
				Message = TempData["SignatureMessage"] as string,
				Panel = !view.AwaitingWitness ? null : new SignaturePanelModel
				{
					Title = text["WitnessPanelTitle"].Value,
					Statement = view.Statement,
					ArtifactSha256 = view.ArtifactSha256,
					RequiredLevel = (int)SignatureLevels.StepUp,
					SignAction = Url.Action("WitnessChecklist", "Signatures", new { area = "User" }),
					Fields = new Dictionary<string, string> { ["id"] = completionId, ["submissionHash"] = view.SubmissionHash },
					CommentField = "attestation",
					SubmitLabel = text["WitnessSignButton"].Value,
					Note = text["WitnessSignatureHelp"].Value,
					Disabled = !view.CanSign,
					DisabledReason = text["WitnessNotYouNote"].Value
				}
			};
			return View("~/Areas/User/Views/Shared/_ChecklistWitnessSignature.cshtml", model);
		}
	}
}

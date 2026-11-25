using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Signatures;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// The external-signer panel (S2) a module page drops in for one subject: "Hand device to signer" (in person, free), the
	/// remote request form where the owning add-on is active, the request's status, the signatures and the sealed PDF. The host
	/// page passes whether the member may act on the subject (its own rule); every POST re-checks it. S3 adds multi-signer
	/// requests: the member's own step (sign at its level, "who else must sign"), every step's state, and — where the module names
	/// one — the option to ask a member to sign in Resgrid (the issue receipt's recipient).
	/// </summary>
	public sealed class SignatureRequestsViewComponent(ISignatureRequestService requests, IDocumentSealingService sealing, IUserProfileService profiles,
		IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> text) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(SignatureSubjectKinds kind, string subjectId, bool canRequest, string returnUrl,
			string suggestedName = null, string suggestedEmail = null, string memberSignerUserId = null, string memberSignerName = null,
			SignatureSignerRoles memberSignerRole = SignatureSignerRoles.Member)
		{
			var model = new SignatureRequestPanelModel
			{
				Kind = kind,
				SubjectId = subjectId,
				CanRequest = canRequest,
				ReturnUrl = returnUrl,
				SuggestedName = suggestedName,
				SuggestedEmail = suggestedEmail,
				MemberSignerUserId = memberSignerUserId,
				MemberSignerName = memberSignerName,
				MemberSignerRole = memberSignerRole,
				SealingConfigured = sealing.GetAvailability()?.Ready == true
			};

			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var userId = ClaimsAuthorizationHelper.GetUserId();
			try
			{
				model.Summary = await requests.GetSubjectSummaryAsync(departmentId, kind, subjectId, userId);
				if (model.Summary == null)
					return Content(string.Empty);

				var shown = model.Open ?? model.Completed;
				if (shown != null)
					model.Steps = model.Summary.Signers.Where(s => s.SignatureRequestId == shown.SignatureRequestId).OrderBy(s => s.SignerOrder).ThenBy(s => s.SignatureRequestSignerId).ToList();
				model.Names = await NamesAsync(model.Summary.Signers);

				// S3: the member's own step in the open request (sign it here; the passkey option is on the signing page).
				if (model.Open != null && model.Steps.Any(s => s.IsMember))
				{
					var view = await requests.GetStepViewAsync(departmentId, model.Open.SignatureRequestId, userId);
					if (view != null && (view.MyStep != null || view.MyWaitingStep != null))
						model.StepPanel = SignatureStepPresentation.Panel(view, model.Names, Url, text, returnUrl, false);
				}
			}
			catch (Exception ex)
			{
				// The module page renders without its signature panel rather than failing.
				Logging.LogException(ex, "The signature panel could not be loaded.");
				return Content(string.Empty);
			}

			var completed = model.Completed;
			var sealedDoc = model.Summary.LatestSealed;
			var urlHelper = Url;
			var signedBy = completed == null ? null : string.Join(", ", model.Summary.Signers.Where(s => s.SignatureRequestId == completed.SignatureRequestId && s.Status == (int)SignatureSignerStatuses.Signed)
				.OrderBy(s => s.SignerOrder).Select(s => s.IsMember ? (s.UserId != null && model.Names.TryGetValue(s.UserId, out var n) ? n : s.StepLabel) : s.ExternalName)
				.Where(n => !string.IsNullOrWhiteSpace(n)));
			model.Panel = new SignaturePanelModel
			{
				Title = text["ExternalPanelTitle"].Value,
				Statement = completed?.StatementText ?? model.Open?.StatementText ?? text["ExternalPanelHelp"].Value,
				ArtifactSha256 = completed?.ArtifactSha256 ?? model.Summary.CurrentRevisionSha256,
				RequiredLevel = completed?.RequiredLevel > 0 ? completed.RequiredLevel : (int)SignatureLevels.External,
				Signed = completed != null,
				SealedPdfUrl = completed != null && sealedDoc != null && sealedDoc.IsSealed ? urlHelper.Action("SealedPdf", "Signatures", new { area = "User", id = sealedDoc.SealedDocumentId }) : null,
				SignedNote = completed == null ? null : string.Format(text["SignedByNote"].Value, signedBy, completed.CompletedOn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"),
				Disabled = !canRequest || model.Summary.CurrentRevisionSha256 == null,
				DisabledReason = model.Summary.CurrentRevisionSha256 == null ? text["SubjectUnavailable"].Value : text["NoRightToRequest"].Value,
				HandDeviceAction = urlHelper.Action("RequestSignature", "Signatures", new { area = "User" }),
				HandDeviceFields = new Dictionary<string, string>
				{
					["kind"] = ((int)kind).ToString(CultureInfo.InvariantCulture),
					["subjectId"] = subjectId,
					["channel"] = ((int)SignatureRequestChannels.InPerson).ToString(CultureInfo.InvariantCulture),
					["returnUrl"] = returnUrl
				}
			};

			return View(model);
		}

		private async Task<IReadOnlyDictionary<string, string>> NamesAsync(IEnumerable<SignatureRequestSigner> signers)
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var id in (signers ?? Enumerable.Empty<SignatureRequestSigner>()).Where(s => s.IsMember && !string.IsNullOrWhiteSpace(s.UserId)).Select(s => s.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var name = (await profiles.GetProfileByUserIdAsync(id))?.FullName?.AsFirstNameLastName;
				if (!string.IsNullOrWhiteSpace(name))
					names[id] = name.Trim();
			}
			return names;
		}
	}
}

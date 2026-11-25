using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Signatures;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// e-Signature S3 on the signature pages: "My signatures" (the member's steps whose turn it is), one request's signing page
	/// (sign the step at its level — L1 in the session, L2 after a fresh step-up or with a passkey bound to the request's hash —
	/// or decline it), member-step requests (the issue receipt's recipient), the issue receipt's subject page and the checklist
	/// witness signature (free with Checklists). The Readiness Pro uses are offered only while the add-on is active.
	/// </summary>
	public partial class SignaturesController
	{
		private readonly IReadinessAccessService _readiness;
		private readonly IWorkOrderAuthorizationService _workOrderAuthorization;
		private readonly IInventoryAuthorizationService _inventoryAuthorization;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUserProfileService _profiles;
		private readonly IChecklistWitnessSignatureService _checklistWitness;
		private readonly IProtectedGrantContext _grant;
		private readonly IPasskeyService _passkeys;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> _checklistText;

		#region My signatures and the step page

		[HttpGet]
		public async Task<IActionResult> Mine()
		{
			Response.Headers["Cache-Control"] = "no-store";
			return View("Mine", new MySignaturesView
			{
				Items = await _requests.GetPendingForUserAsync(DepartmentId, UserId),
				Message = TempData["SignatureMessage"] as string
			});
		}

		[HttpGet]
		public async Task<IActionResult> Step(int id, string returnUrl)
		{
			var view = await _requests.GetStepViewAsync(DepartmentId, id, UserId);
			if (view == null)
				return NotFound();
			var isSigner = view.MyStep != null || view.MyWaitingStep != null || view.Signers.Any(s => SameUser(s.UserId, UserId));
			var canManage = await CanActOnRequestAsync(view.Request);
			if (!isSigner && !canManage)
				return NotFound();

			Response.Headers["Cache-Control"] = "no-store";
			var back = LocalReturn(returnUrl ?? Url.Action("Mine", "Signatures", new { area = "User" }));
			var names = await NamesAsync(view.Signers);
			return View("Step", new SignatureStepPageView
			{
				Step = view,
				Names = names,
				Panel = view.MyStep != null || view.MyWaitingStep != null ? SignatureStepPresentation.Panel(view, names, Url, _text, back, _passkeys != null) : null,
				CanManage = canManage,
				CanDecline = view.Request.IsOpen && view.Signers.Any(s => s.IsMember && !s.IsDone && SameUser(s.UserId, UserId)),
				SubjectUrl = canManage ? SignatureStepPresentation.SubjectUrl(Url, (SignatureSubjectKinds)view.Request.SubjectKind, view.Request.SubjectId) : null,
				ReturnUrl = back,
				Message = TempData["SignatureMessage"] as string
			});
		}

		/// <summary>An L1 step: the signed-in session (an L2 step is refused here and asks for the step-up path).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SignStep(int id, int signerId, string returnUrl, CancellationToken cancellationToken) =>
			SignStepCoreAsync(id, signerId, returnUrl, SessionProof(), cancellationToken);

		/// <summary>An L2 step after a fresh step-up (the form is held across the step-up and replayed).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public Task<IActionResult> SignStepUp(int id, int signerId, string returnUrl, CancellationToken cancellationToken)
		{
			var proof = SessionProof();
			proof.StepUpVerifiedOnUtc = HttpContext?.Items["mfa_verified_at"] as DateTime?;
			return SignStepCoreAsync(id, signerId, returnUrl, proof, cancellationToken);
		}

		/// <summary>Passkey options whose WebAuthn challenge embeds the request's artifact hash (resolved here, never posted).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> StepPasskeyOptions(string purpose, int id, CancellationToken cancellationToken)
		{
			var view = purpose == "step" ? await _requests.GetStepViewAsync(DepartmentId, id, UserId) : null;
			var caller = await PasskeyCallerAsync();
			if (_passkeys == null || view?.MyStep == null || caller == null)
				return Json(new { success = false, error = "invalid_request" });

			var start = await _passkeys.BeginSigningAssertionAsync(caller, view.Request.ArtifactSha256, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, requestId = start.RequestId, options = start.OptionsJson })
				: Json(new { success = false, error = PasskeyOutcomes.ErrorCode(start.Outcome) });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SignStepWithPasskey(int id, int signerId, string passkeyRequestId, string passkeyCredential, string returnUrl, CancellationToken cancellationToken)
		{
			var proof = SessionProof();
			var request = await _requests.GetAsync(DepartmentId, id);
			var caller = await PasskeyCallerAsync();
			if (request != null && caller != null && _passkeys != null && !string.IsNullOrWhiteSpace(passkeyRequestId) && !string.IsNullOrWhiteSpace(passkeyCredential))
			{
				var assertion = await _passkeys.CompleteSigningAssertionAsync(caller, passkeyRequestId, passkeyCredential, request.ArtifactSha256, cancellationToken);
				if (assertion.Succeeded)
				{
					proof.PasskeyId = assertion.Passkey.UserPasskeyId;
					proof.PasskeyChallengeId = passkeyRequestId;
					proof.PasskeyBoundSha256 = request.ArtifactSha256;
					proof.PasskeyVerifiedOnUtc = assertion.VerifiedOnUtc;
				}
			}

			return await SignStepCoreAsync(id, signerId, returnUrl, proof, cancellationToken);
		}

		private async Task<IActionResult> SignStepCoreAsync(int id, int signerId, string returnUrl, SignatureProof proof, CancellationToken cancellationToken)
		{
			var back = LocalReturn(returnUrl);
			var result = await _requests.SignStepAsync(new SignatureStepSignInput
			{
				DepartmentId = DepartmentId,
				SignatureRequestId = id,
				SignatureRequestSignerId = signerId > 0 ? signerId : (int?)null,
				Proof = proof
			}, cancellationToken);

			TempData["SignatureMessage"] = result.Succeeded ? _text["StepSignedMessage"].Value : RefusalText(result.Refusal);
			return result.Succeeded ? Redirect(back) : RedirectToAction("Step", new { id, returnUrl = back });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeclineStep(int id, int signerId, string reason, string returnUrl, CancellationToken cancellationToken)
		{
			var result = await _requests.DeclineStepAsync(DepartmentId, id, signerId > 0 ? signerId : (int?)null, UserId, reason, cancellationToken);
			TempData["SignatureMessage"] = result.Succeeded ? _text["StepDeclinedMessage"].Value : RefusalText(result.Refusal);
			return RedirectToAction("Step", new { id, returnUrl = LocalReturn(returnUrl) });
		}

		#endregion

		#region Member-step requests and the subject page

		/// <summary>
		/// A request a department member signs in Resgrid (the issue receipt's member recipient at L2). Remote and in-person outside
		/// signers keep the S2 request form.
		/// </summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RequestMemberSignature(SignatureSubjectKinds kind, string subjectId, string memberUserId, SignatureSignerRoles role, string returnUrl,
			CancellationToken cancellationToken)
		{
			var back = LocalReturn(returnUrl);
			if (!await CanActOnAsync(kind, subjectId))
				return Unauthorized();
			if (!await UseAvailableAsync(kind))
			{
				TempData["SignatureMessage"] = _text["ReadinessProRequiredMessage"].Value;
				return Redirect(back);
			}

			// The issue receipt is signed by the member the equipment was issued to, never someone the form names.
			if (kind == SignatureSubjectKinds.InventoryIssueReceipt)
			{
				var info = await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId);
				if (info == null || string.IsNullOrWhiteSpace(info.ContextId))
					return Redirect(back);
				memberUserId = info.ContextId;
				role = SignatureSignerRoles.Recipient;
			}

			var result = await _requests.CreateMultiSignerAsync(new MultiSignerRequestInput
			{
				DepartmentId = DepartmentId,
				SubjectKind = kind,
				SubjectId = subjectId,
				Ordered = true,
				Steps = new List<SignatureStepInput>
				{
					new SignatureStepInput { Kind = SignatureSignerKinds.Member, Role = SignatureOrdering.IsMemberRole(role) ? role : SignatureSignerRoles.Member, Order = 1, UserId = memberUserId, Level = SignatureLevels.StepUp }
				},
				Culture = CultureInfo.CurrentUICulture.Name,
				RequestedByUserId = UserId,
				IpAddress = Ip,
				UserAgent = Agent
			}, cancellationToken);

			TempData["SignatureMessage"] = result.Succeeded ? _text["MemberRequestSentMessage"].Value : RefusalText(result.Refusal);
			return Redirect(back);
		}

		/// <summary>A subject without a page of its own (the issue receipt): what is signed, and the shared signature panel.</summary>
		[HttpGet]
		public async Task<IActionResult> Subject(SignatureSubjectKinds kind, string subjectId, string returnUrl)
		{
			if (kind != SignatureSubjectKinds.InventoryIssueReceipt || !await CanActOnAsync(kind, subjectId))
				return NotFound();
			var info = await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId);
			if (info == null)
				return NotFound();

			var view = new SignatureSubjectPageView
			{
				Kind = kind,
				SubjectId = subjectId,
				Info = info,
				CanRequest = await UseAvailableAsync(kind),
				ReturnUrl = LocalReturn(returnUrl ?? Url.Action("Index", "Inventory", new { area = "User", tab = "Issuances" })),
				MemberSignerUserId = info.ContextId,
				MemberSignerName = info.SuggestedSignerName
			};
			return View("Subject", view);
		}

		#endregion

		#region Checklist witness signature (free with Checklists)

		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> WitnessChecklist(string id, string submissionHash, string attestation, CancellationToken cancellationToken)
		{
			var proof = SessionProof();
			proof.StepUpVerifiedOnUtc = HttpContext?.Items["mfa_verified_at"] as DateTime?;
			string message;
			try
			{
				var outcome = await _checklistWitness.SignAsync(new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = _grant.GrantToken }, id, submissionHash,
					attestation, proof, CultureInfo.CurrentUICulture.Name, cancellationToken);
				message = outcome == SignatureOutcomes.Signed ? _text["WitnessSignedMessage"].Value : _text["Outcome" + outcome].Value;
			}
			catch (ChecklistException ex)
			{
				var text = _checklistText[ex.Message];
				message = text.ResourceNotFound ? _text["WitnessSignFailedMessage"].Value : text.Value;
			}

			TempData["SignatureMessage"] = message;
			return RedirectToAction("CompletionDetail", "Checklists", new { area = "User", id });
		}

		#endregion

		#region Helpers

		/// <summary>The Readiness Pro uses (work-order vendor acceptance, issue receipts) exist only while the add-on is active.</summary>
		private async Task<bool> UseAvailableAsync(SignatureSubjectKinds kind) =>
			kind != SignatureSubjectKinds.WorkOrderVendorAcceptance && kind != SignatureSubjectKinds.InventoryIssueReceipt || await _readiness.CanUseMaintenanceAsync(DepartmentId);

		/// <summary>
		/// Work orders: the member reads the order in their scope and may manage its group (the work-order rule). Issue receipts: the
		/// member reads the issue and holds the Issue Inventory permission (the Issuances tab's rule).
		/// </summary>
		private async Task<bool> CanActOnReadinessAsync(SignatureSubjectKinds kind, string subjectId)
		{
			var info = await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId);
			if (info == null)
				return false;

			if (kind == SignatureSubjectKinds.WorkOrderVendorAcceptance)
			{
				int? group = int.TryParse(info.ContextId, NumberStyles.None, CultureInfo.InvariantCulture, out var g) ? g : (int?)null;
				return IsAdmin || await _workOrderAuthorization.CanManageAsync(new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = _grant.GrantToken }, group);
			}

			var actorGroupId = (await _groups.GetGroupForUserAsync(UserId, DepartmentId))?.DepartmentGroupId;
			try
			{
				await _inventoryAuthorization.RequireAsync(new InventoryActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = _grant.GrantToken }, true, PermissionTypes.IssueInventory, actorGroupId);
				return true;
			}
			catch (InventoryException ex) when (ex.StatusCode is 403 or 404 or 409)
			{
				return false;
			}
		}

		/// <summary>
		/// A request's own actions (detail, resend, withdraw, hand device): the subject's module rule (T4 registers its own for
		/// SkillEvaluation); for an evaluation also the member who requested it.
		/// </summary>
		private async Task<bool> CanActOnRequestAsync(SignatureRequestRecord request) =>
			request != null && (await CanActOnAsync((SignatureSubjectKinds)request.SubjectKind, request.SubjectId)
				|| request.SubjectKind == (int)SignatureSubjectKinds.SkillEvaluation && SameUser(request.CreatedByUserId, UserId));

		private SignatureProof SessionProof() => SignatureProof.SessionOnly(UserId, Ip, Agent);

		private async Task<PasskeyCaller> PasskeyCallerAsync()
		{
			var user = _userManager == null ? null : await _userManager.GetUserAsync(User);
			return user == null ? null : PasskeyCaller.From(HttpProtectedGrantContext.SessionOf(HttpContext), user.Id, user.UserName, DepartmentId,
				SystemAuditSystems.Website, IpAddressHelper.GetRequestIP(Request, true));
		}

		/// <summary>Display names of the member signers (a few profile reads; names are operational, never contact details).</summary>
		private async Task<IReadOnlyDictionary<string, string>> NamesAsync(IEnumerable<SignatureRequestSigner> signers)
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var userId in (signers ?? Enumerable.Empty<SignatureRequestSigner>()).Where(s => s.IsMember && !string.IsNullOrWhiteSpace(s.UserId)).Select(s => s.UserId).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				try
				{
					var profile = await _profiles.GetProfileByUserIdAsync(userId);
					var name = profile?.FullName?.AsFirstNameLastName;
					if (!string.IsNullOrWhiteSpace(name))
						names[userId] = name.Trim();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A signer's name could not be read.");
				}
			}
			return names;
		}

		private static bool SameUser(string a, string b) =>
			!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

		#endregion
	}
}

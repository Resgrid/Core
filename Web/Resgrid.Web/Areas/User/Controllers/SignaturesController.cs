using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Signatures;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;
using AspNetAuthorization = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// External signers (e-Signature S2): request a customer, occupant or outside party's signature on a module's document — by a
	/// Resgrid signing link with a one-time code (where the owning add-on is active, decision 19) or in person on this device
	/// (free everywhere) — and follow, resend or withdraw the request. Every action re-checks the member's right to act on the
	/// subject in its own module; the e-sign platform account (Documenso team token) is a department-admin setting.
	/// </summary>
	[Area("User")]
	[Authorize]
	[DepartmentLocalTime]
	public partial class SignaturesController : SecureBaseController
	{
		private readonly ISignatureRequestService _requests;
		private readonly IDocumentSealingService _sealing;
		private readonly IESignAccountService _accounts;
		private readonly IFeatureToggleService _flags;
		private readonly ITimeTrackingService _timeTracking;
		private readonly IDeploymentService _deployments;
		private readonly IBusinessOperationsAccessService _businessOperations;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsInvestigationsService _investigations;
		private readonly IDepartmentsService _departments;
		private readonly AspNetAuthorization _authorization;
		private readonly IStringLocalizer<Signatures> _text;
		private readonly IReadOnlyList<ISignatureSubjectAccessRule> _accessRules;

		public SignaturesController(ISignatureRequestService requests, IDocumentSealingService sealing, IESignAccountService accounts, IFeatureToggleService flags,
			ITimeTrackingService timeTracking, IDeploymentService deployments, IBusinessOperationsAccessService businessOperations, IRecordsCutoverService cutover,
			IRecordsInvestigationsService investigations, IDepartmentsService departments, AspNetAuthorization authorization, IStringLocalizer<Signatures> text,
			IReadinessAccessService readiness, IWorkOrderAuthorizationService workOrderAuthorization, IInventoryAuthorizationService inventoryAuthorization,
			IDepartmentGroupsService groups, IUserProfileService profiles, IChecklistWitnessSignatureService checklistWitness, IProtectedGrantContext grant,
			IPasskeyService passkeys, Microsoft.AspNetCore.Identity.UserManager<Resgrid.Model.Identity.IdentityUser> userManager,
			IStringLocalizer<Resgrid.Localization.Areas.User.Checklists.Checklists> checklistText, IEnumerable<ISignatureSubjectAccessRule> accessRules)
		{
			_accessRules = (accessRules ?? Enumerable.Empty<ISignatureSubjectAccessRule>()).ToList();
			_readiness = readiness;
			_workOrderAuthorization = workOrderAuthorization;
			_inventoryAuthorization = inventoryAuthorization;
			_groups = groups;
			_profiles = profiles;
			_checklistWitness = checklistWitness;
			_grant = grant;
			_passkeys = passkeys;
			_userManager = userManager;
			_checklistText = checklistText;
			_requests = requests;
			_sealing = sealing;
			_accounts = accounts;
			_flags = flags;
			_timeTracking = timeTracking;
			_deployments = deployments;
			_businessOperations = businessOperations;
			_cutover = cutover;
			_investigations = investigations;
			_departments = departments;
			_authorization = authorization;
			_text = text;
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => Request.Headers["User-Agent"].ToString();
		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		#region Requests

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RequestSignature(SignatureSubjectKinds kind, string subjectId, SignatureRequestChannels channel, string signerName, string signerEmail,
			string signerPhone, SignatureSignerAuthMethods otpChannel, string returnUrl, CancellationToken cancellationToken)
		{
			var back = LocalReturn(returnUrl);
			if (!await CanActOnAsync(kind, subjectId))
				return Unauthorized();
			// S3: the Readiness Pro uses exist only while the add-on is active (remote L4 is gated again in the service).
			if (!await UseAvailableAsync(kind))
			{
				TempData["SignatureMessage"] = _text["ReadinessProRequiredMessage"].Value;
				return Redirect(back);
			}

			var result = await _requests.CreateAsync(new ExternalSignatureRequestInput
			{
				DepartmentId = DepartmentId,
				SubjectKind = kind,
				SubjectId = subjectId,
				Channel = channel,
				SignerName = signerName,
				SignerEmail = signerEmail,
				SignerPhone = signerPhone,
				OtpChannel = otpChannel == SignatureSignerAuthMethods.SmsOtp ? SignatureSignerAuthMethods.SmsOtp : SignatureSignerAuthMethods.EmailOtp,
				Culture = CultureInfo.CurrentUICulture.Name,
				RequestedByUserId = UserId,
				IpAddress = Ip,
				UserAgent = Agent
			}, cancellationToken);

			if (!result.Succeeded)
			{
				TempData["SignatureMessage"] = RefusalText(result.Refusal);
				return Redirect(back);
			}

			if (channel == SignatureRequestChannels.InPerson)
				return RedirectToAction("HandDevice", new { id = result.Request.SignatureRequestId, returnUrl = back });

			TempData["SignatureMessage"] = result.Signer?.LinkSentOn.HasValue == true
				? string.Format(_text["RequestSentMessage"].Value, result.Signer.ContactMasked)
				: _text["RequestNotDeliveredMessage"].Value;
			if (result.Signer?.LinkSentOn.HasValue != true && !string.IsNullOrEmpty(result.LinkToken))
				TempData["SignatureLink"] = SigningUrl(result.LinkToken);
			return RedirectToAction("Detail", new { id = result.Request.SignatureRequestId, returnUrl = back });
		}

		[HttpGet]
		public async Task<IActionResult> Detail(int id, string returnUrl)
		{
			var request = await _requests.GetAsync(DepartmentId, id);
			if (request == null || !await CanActOnRequestAsync(request))
				return NotFound();

			var signers = await _requests.GetSignersAsync(DepartmentId, id);
			return View("Detail", new SignatureRequestDetailView
			{
				Request = request,
				Signers = signers,
				Names = await NamesAsync(signers),
				Sealed = request.SealedDocumentId.HasValue ? await _sealing.GetByIdAsync(DepartmentId, request.SealedDocumentId.Value) : null,
				ProviderCompleted = await _sealing.GetLatestAsync(DepartmentId, (SignatureSubjectKinds)request.SubjectKind, request.SubjectId, request.ArtifactSha256, SealedDocumentRoles.ProviderCompleted),
				ReturnUrl = LocalReturn(returnUrl),
				Message = TempData["SignatureMessage"] as string,
				LinkUrl = TempData["SignatureLink"] as string
			});
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Resend(int id, string returnUrl, CancellationToken cancellationToken)
		{
			var request = await _requests.GetAsync(DepartmentId, id);
			if (request == null || !await CanActOnRequestAsync(request))
				return NotFound();

			var result = await _requests.ResendAsync(DepartmentId, id, UserId, cancellationToken);
			TempData["SignatureMessage"] = !result.Succeeded ? RefusalText(result.Refusal)
				: result.Signer?.LinkSentOn.HasValue == true ? string.Format(_text["RequestSentMessage"].Value, result.Signer.ContactMasked) : _text["RequestNotDeliveredMessage"].Value;
			if (result.Succeeded && result.Signer?.LinkSentOn.HasValue != true && !string.IsNullOrEmpty(result.LinkToken))
				TempData["SignatureLink"] = SigningUrl(result.LinkToken);
			return RedirectToAction("Detail", new { id, returnUrl = LocalReturn(returnUrl) });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Void(int id, string reason, string returnUrl, CancellationToken cancellationToken)
		{
			var request = await _requests.GetAsync(DepartmentId, id);
			if (request == null || !await CanActOnRequestAsync(request))
				return NotFound();

			await _requests.VoidAsync(DepartmentId, id, UserId, string.IsNullOrWhiteSpace(reason) ? "withdrawn" : reason, cancellationToken);
			TempData["SignatureMessage"] = _text["RequestVoidedMessage"].Value;
			return Redirect(LocalReturn(returnUrl));
		}

		/// <summary>A request's sealed evidence or the platform's completed PDF, for whoever may act on its subject.</summary>
		[HttpGet]
		public async Task<IActionResult> SealedPdf(int id, CancellationToken cancellationToken)
		{
			var document = await _sealing.GetByIdAsync(DepartmentId, id);
			if (document == null || ExternalSignatureUse.For((SignatureSubjectKinds)document.SubjectKind) == null
				|| !await CanActOnAsync((SignatureSubjectKinds)document.SubjectKind, document.SubjectId))
				return NotFound();

			var bytes = await _sealing.OpenAsync(DepartmentId, id, cancellationToken);
			if (bytes == null)
				return NotFound();

			Response.Headers["Cache-Control"] = "no-store";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			var pdf = SealingPolicy.LooksLikePdf(bytes);
			return File(bytes, pdf ? "application/pdf" : "application/json", document.FileName ?? (pdf ? "sealed.pdf" : "audit.json"));
		}

		#endregion

		#region In person ("Hand device to signer")

		[HttpGet]
		public async Task<IActionResult> HandDevice(int id, string returnUrl)
		{
			var request = await _requests.GetAsync(DepartmentId, id);
			if (request == null || request.Channel != (int)SignatureRequestChannels.InPerson || !await CanActOnRequestAsync(request))
				return NotFound();

			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			Response.Headers["Cache-Control"] = "no-store";
			return View(new HandDeviceView
			{
				Request = request,
				DepartmentName = department?.Name,
				ReturnUrl = LocalReturn(returnUrl),
				ConsentText = _text["ConsentText"].Value,
				ConsentVersion = SignatureTokens.ConsentTextVersion,
				Message = TempData["SignatureMessage"] as string
			});
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequestSizeLimit(2 * 1024 * 1024)]
		public async Task<IActionResult> HandDevice(int id, string boundRevisionSha256, string printedName, string signatureImage, string witnessName, bool consent,
			string returnUrl, CancellationToken cancellationToken)
		{
			var request = await _requests.GetAsync(DepartmentId, id);
			if (request == null || !await CanActOnRequestAsync(request))
				return NotFound();

			var result = await _requests.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = DepartmentId,
				SignatureRequestId = id,
				BoundRevisionSha256 = boundRevisionSha256,
				PrintedName = printedName,
				SignatureImageDataUrl = signatureImage,
				WitnessName = witnessName,
				ConsentGiven = consent,
				CapturedByUserId = UserId,
				IpAddress = Ip,
				UserAgent = Agent
			}, cancellationToken);

			if (!result.Succeeded)
			{
				TempData["SignatureMessage"] = RefusalText(result.Refusal);
				return result.Refusal == SignatureRequestRefusals.InvalidRequest
					? RedirectToAction("HandDevice", new { id, returnUrl = LocalReturn(returnUrl) })
					: Redirect(LocalReturn(returnUrl));
			}

			TempData["SignatureMessage"] = _text["InPersonSignedMessage"].Value;
			return Redirect(LocalReturn(returnUrl));
		}

		#endregion

		#region E-sign platform account (department admin)

		[HttpGet]
		public async Task<IActionResult> ESign()
		{
			if (!IsAdmin)
				return Unauthorized();
			var account = await _accounts.GetAsync(DepartmentId);
			return View(new ESignSettingsView
			{
				PlatformConfigured = _accounts.IsPlatformConfigured(),
				Sealing = _sealing.GetAvailability(),
				TeamId = account?.TeamId,
				TokenHint = account?.TokenHint,
				HasToken = !string.IsNullOrEmpty(account?.ApiTokenEncrypted),
				IsActive = account?.IsActive ?? true,
				Saved = TempData["SignatureSaved"] is bool saved && saved,
				Message = TempData["SignatureMessage"] as string
			});
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor]
		public async Task<IActionResult> ESign(string teamId, string apiToken, bool isActive, bool remove, CancellationToken cancellationToken)
		{
			if (!IsAdmin)
				return Unauthorized();

			if (remove)
				await _accounts.RemoveAsync(DepartmentId, UserId, cancellationToken);
			else
				await _accounts.SaveAsync(DepartmentId, teamId, apiToken, isActive, UserId, cancellationToken);

			TempData["SignatureSaved"] = true;
			return RedirectToAction("ESign");
		}

		#endregion

		#region Access

		/// <summary>The member may act on the subject in its own module (the same rule its page applies).</summary>
		[NonAction]
		public async Task<bool> CanActOnAsync(SignatureSubjectKinds kind, string subjectId)
		{
			if (string.IsNullOrWhiteSpace(subjectId))
				return false;

			try
			{
				switch (kind)
				{
					case SignatureSubjectKinds.TimeReport:
					{
						if (!await _flags.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId))
							return false;
						var report = await _timeTracking.GetTimeReportByIdAsync(subjectId, DepartmentId);
						if (report == null || report.DepartmentId != DepartmentId)
							return false;
						var deployment = await _deployments.GetDeploymentByIdAsync(report.DeploymentId, DepartmentId);
						if (deployment == null)
							return false;
						var canManage = IsAdmin || ClaimsAuthorizationHelper.CanManageDeployments();
						return (await _deployments.GetTimeAccessAsync(deployment, UserId, canManage)).CanActOn(report);
					}
					case SignatureSubjectKinds.Bid:
						return (IsAdmin || ClaimsAuthorizationHelper.CanManageBids()) && await _businessOperations.CanUseContractorBillingAsync(DepartmentId);
					case SignatureSubjectKinds.ServiceContract:
						return (IsAdmin || ClaimsAuthorizationHelper.CanManageContracts()) && await _businessOperations.CanUseContractorBillingAsync(DepartmentId);
					case SignatureSubjectKinds.RecordsInspection:
						return await RecordsModuleOnAsync(FeatureFlagKeys.RecordsPreventionInspections)
							&& (await _authorization.AuthorizeAsync(User, ResgridResources.Record_PreventionAdmin)).Succeeded
							&& await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId) != null;
					case SignatureSubjectKinds.CustodyTransfer:
					{
						if (!await RecordsModuleOnAsync(FeatureFlagKeys.RecordsInvestigations) || !(await _authorization.AuthorizeAsync(User, ResgridResources.RecordRestricted_View)).Succeeded)
							return false;
						var info = await _requests.DescribeSubjectAsync(DepartmentId, kind, subjectId, UserId);
						if (info?.ContextId == null)
							return false;
						// The investigation's own rule: the chain is readable only by the case's members.
						var chain = await _investigations.GetCustodyChainAsync(DepartmentId, UserId, info.ContextId) ?? new List<RmsInvestigationCustody>();
						return chain.Any(c => string.Equals(c.RmsInvestigationCustodyId, subjectId, StringComparison.OrdinalIgnoreCase));
					}
					case SignatureSubjectKinds.WorkOrderVendorAcceptance:
					case SignatureSubjectKinds.InventoryIssueReceipt:
						return await CanActOnReadinessAsync(kind, subjectId);
					default:
					{
						// A module's own rule (T4 registers SkillEvaluation: LMS managers, the evaluator or requester, a task book's
						// initiator). Checklist witnesses are reached through their own pages; a step's signer reaches the request through Step.
						var rule = _accessRules.FirstOrDefault(r => r.Kind == kind);
						return rule != null && await rule.CanActOnAsync(DepartmentId, UserId, subjectId);
					}
				}
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Signature subject access could not be checked.");
				return false;
			}
		}

		private async Task<bool> RecordsModuleOnAsync(string flag)
		{
			var state = await _cutover.GetModuleStateAsync(DepartmentId);
			return state != null && state.FlagEnabled && await _flags.IsEnabledAsync(flag, DepartmentId);
		}

		private string RefusalText(SignatureRequestRefusals refusal) => _text["Refusal" + refusal].Value;

		private string SigningUrl(string token) => (Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl ?? GetBaseUrl()).TrimEnd('/') + "/Sign/" + token;

		private string LocalReturn(string returnUrl) =>
			!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "Home", new { area = "User" }) ?? "/User/Home/Index";

		#endregion
	}
}

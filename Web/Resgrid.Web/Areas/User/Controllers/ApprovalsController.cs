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
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Approvals;
using Resgrid.Web.Areas.User.Models.Signatures;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// e-Signature S4 approval chains: "My approvals" (requests waiting for the member), one request's page (the subject's document, the
	/// steps, approving at L2 through the shared signature panel — a fresh step-up here, or a passkey bound to the content on the
	/// signing page — and rejecting with a reason), submitting a subject, the department's chains and the members' delegates
	/// (department admins), and mutual-aid agreement documents. Each use is offered only while its module and add-on are active.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class ApprovalsController : SecureBaseController
	{
		private readonly IApprovalChainService _approvals;
		private readonly IMutualAidAgreementService _agreements;
		private readonly IDepartmentsService _departments;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentGroupsService _groups;
		private readonly ICertificationService _certifications;
		private readonly IDepartmentLinksService _links;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Approvals.Approvals> _text;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> _signatureText;
		private readonly IMutualAidCountersignService _countersign;

		public ApprovalsController(IApprovalChainService approvals, IMutualAidAgreementService agreements, IDepartmentsService departments, IPersonnelRolesService roles,
			IDepartmentGroupsService groups, ICertificationService certifications, IDepartmentLinksService links,
			IStringLocalizer<Resgrid.Localization.Areas.User.Approvals.Approvals> text, IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> signatureText,
			IMutualAidCountersignService countersign)
		{
			_countersign = countersign;
			_approvals = approvals;
			_agreements = agreements;
			_departments = departments;
			_roles = roles;
			_groups = groups;
			_certifications = certifications;
			_links = links;
			_text = text;
			_signatureText = signatureText;
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => Request.Headers["User-Agent"].ToString();
		private bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		private string RefusalText(ApprovalRefusals refusal) => _text["Refusal" + refusal].Value;

		private async Task<Dictionary<string, string>> NamesAsync() =>
			(await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.GroupBy(p => p.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		#region My approvals and requests

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			Response.Headers["Cache-Control"] = "no-store";
			var kinds = new List<SignatureSubjectKinds>();
			foreach (var kind in _approvals.AvailableKinds)
				if (await _approvals.IsKindAvailableAsync(DepartmentId, kind) && await _approvals.GetActiveChainAsync(DepartmentId, kind) != null)
					kinds.Add(kind);
			return View(new ApprovalsInboxView
			{
				Items = await _approvals.GetInboxAsync(DepartmentId, UserId),
				Pending = (await _approvals.GetRequestsAsync(DepartmentId, ApprovalRequestStatuses.Pending, 100)).Where(r => IsAdmin || ApprovalRouting.Same(r.SubmittedByUserId, UserId)).ToList(),
				Kinds = kinds,
				IsAdmin = IsAdmin,
				Message = TempData["ApprovalMessage"] as string
			});
		}

		[HttpGet]
		public async Task<IActionResult> Requests(ApprovalRequestStatuses status = ApprovalRequestStatuses.Approved)
		{
			if (!IsAdmin)
				return Unauthorized();
			return View(new ApprovalRequestsView { Status = status, Requests = await _approvals.GetRequestsAsync(DepartmentId, status, 200), Names = await NamesAsync() });
		}

		[HttpGet]
		public async Task<IActionResult> Detail(int id)
		{
			var view = await _approvals.GetViewAsync(DepartmentId, id, UserId, IsAdmin);
			if (view == null)
				return NotFound();
			Response.Headers["Cache-Control"] = "no-store";

			var names = await NamesAsync();
			SignaturePanelModel panel = null;
			if (view.StepView != null && view.Request.IsPending && (view.StepView.MyStep != null || view.StepView.MyWaitingStep != null))
			{
				var back = Url.Action("Detail", "Approvals", new { area = "User", id });
				panel = SignatureStepPresentation.Panel(view.StepView, names, Url, _signatureText, back, true);
				// The step-up approval posts here (the approval layer announces the next step at once); the passkey path is the shared
				// signing page's, bound to the same content hash, and worker 97 announces after it.
				panel.Title = _text["ApprovePanelTitle"].Value;
				panel.SubmitLabel = _text["ApproveButton"].Value;
				panel.SignAction = Url.Action("Approve", "Approvals", new { area = "User" });
				panel.Fields["approvalId"] = id.ToString(CultureInfo.InvariantCulture);
				if (view.Changed)
				{
					panel.Disabled = true;
					panel.DisabledReason = RefusalText(ApprovalRefusals.SubjectChanged);
				}
			}

			return View(new ApprovalDetailView
			{
				View = view,
				Panel = panel,
				Names = names,
				SubjectUrl = SubjectUrl((SignatureSubjectKinds)view.Request.SubjectKind, view.Request.SubjectId),
				Message = TempData["ApprovalMessage"] as string,
				Error = TempData["ApprovalError"] as string
			});
		}

		/// <summary>Approve the member's step at L2 after a fresh step-up (the form is held across the step-up and replayed).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, StepUpArea = Resgrid.Model.Security.StepUpArea.Approvals)]
		public async Task<IActionResult> Approve(int approvalId, CancellationToken cancellationToken)
		{
			var proof = SignatureProof.SessionOnly(UserId, Ip, Agent);
			Resgrid.Web.Helpers.WebStepUpProof.Apply(proof, HttpContext);
			var result = await _approvals.ApproveAsync(DepartmentId, approvalId, proof, false, cancellationToken);
			TempData[result.Succeeded ? "ApprovalMessage" : "ApprovalError"] = result.Succeeded ? _text["ApprovedMessage"].Value : RefusalText(result.Refusal);
			return RedirectToAction("Detail", new { id = approvalId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Reject(int id, string reason, CancellationToken cancellationToken)
		{
			var result = await _approvals.RejectAsync(DepartmentId, id, UserId, reason, cancellationToken);
			TempData[result.Succeeded ? "ApprovalMessage" : "ApprovalError"] = result.Succeeded ? _text["RejectedMessage"].Value : RefusalText(result.Refusal);
			return RedirectToAction("Detail", new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken)
		{
			var result = await _approvals.WithdrawAsync(DepartmentId, id, UserId, IsAdmin, cancellationToken);
			TempData[result.Succeeded ? "ApprovalMessage" : "ApprovalError"] = result.Succeeded ? _text["WithdrawnMessage"].Value : RefusalText(result.Refusal);
			return RedirectToAction("Detail", new { id });
		}

		#endregion

		#region Submit

		[HttpGet]
		public async Task<IActionResult> Submit(SignatureSubjectKinds kind, string subjectId)
		{
			if (!ApprovalUses.IsApprovalKind(kind))
				return NotFound();
			var view = new ApprovalSubmitView { Kind = kind, Available = await _approvals.IsKindAvailableAsync(DepartmentId, kind), Error = TempData["ApprovalError"] as string };
			view.ChainActive = view.Available && await _approvals.GetActiveChainAsync(DepartmentId, kind) != null;
			if (!string.IsNullOrWhiteSpace(subjectId))
			{
				view.Status = await _approvals.GetSubjectStatusAsync(DepartmentId, kind, subjectId, UserId, IsAdmin);
				if (view.Status.Subject == null)
					return NotFound();
			}
			else if (view.ChainActive)
			{
				view.Candidates = (await _approvals.GetCandidatesAsync(DepartmentId, kind, 50)).Where(c => IsAdmin || ApprovalRouting.Same(c.OwnerUserId, UserId)).ToList();
			}
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		[ActionName("Submit")]
		public async Task<IActionResult> SubmitPost(SignatureSubjectKinds kind, string subjectId, CancellationToken cancellationToken)
		{
			var result = await _approvals.SubmitAsync(new ApprovalSubmitInput
			{
				DepartmentId = DepartmentId,
				Kind = kind,
				SubjectId = subjectId,
				UserId = UserId,
				IsDepartmentAdmin = IsAdmin,
				Culture = CultureInfo.CurrentUICulture.Name,
				IpAddress = Ip,
				UserAgent = Agent
			}, cancellationToken);
			if (!result.Succeeded)
			{
				TempData["ApprovalError"] = RefusalText(result.Refusal);
				return RedirectToAction("Submit", new { kind, subjectId });
			}
			TempData["ApprovalMessage"] = _text["SubmittedMessage"].Value;
			return RedirectToAction("Detail", new { id = result.Request.ApprovalRequestId });
		}

		#endregion

		#region Chains and delegates (department admins)

		[HttpGet]
		public async Task<IActionResult> Chains()
		{
			if (!IsAdmin)
				return Unauthorized();
			var view = new ApprovalChainsView { Chains = await _approvals.GetChainsAsync(DepartmentId), Kinds = _approvals.AvailableKinds.ToList(), Message = TempData["ApprovalMessage"] as string };
			foreach (var kind in view.Kinds)
				view.Available[kind] = await _approvals.IsKindAvailableAsync(DepartmentId, kind);
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> EditChain(SignatureSubjectKinds kind, int id = 0)
		{
			if (!IsAdmin)
				return Unauthorized();
			var model = new ApprovalChainEditView { Kind = kind, Name = _text["Kind" + kind].Value };
			var definition = id > 0 ? await _approvals.GetChainAsync(DepartmentId, id) : await _approvals.GetActiveChainAsync(DepartmentId, kind);
			if (definition != null)
			{
				model.ApprovalChainId = definition.Chain.ApprovalChainId;
				model.Kind = (SignatureSubjectKinds)definition.Chain.SubjectKind;
				model.Name = definition.Chain.Name;
				model.IsActive = definition.Chain.IsActive;
				model.AllowSelfApproval = definition.Chain.AllowSelfApproval;
				model.ExpiresInDays = definition.Chain.ExpiresInDays;
				model.Steps = definition.Steps.Select(s => new ApprovalChainStepRow
				{
					Include = true, StepOrder = s.StepOrder, ApproverKind = (ApprovalApproverKinds)s.ApproverKind, PersonnelRoleId = s.PersonnelRoleId, UserId = s.UserId,
					DepartmentGroupId = s.DepartmentGroupId, CertificationTypeId = s.CertificationTypeId, MinimumAmount = s.MinimumAmount, Label = s.Label,
					EscalateAfterHours = s.EscalateAfterHours, EscalateTo = (ApprovalEscalationTargets)s.EscalateTo, EscalateToRoleId = s.EscalateToRoleId, EscalateToUserId = s.EscalateToUserId
				}).ToList();
			}
			if (!ApprovalUses.IsApprovalKind(model.Kind))
				return NotFound();
			await FillPickersAsync(model);
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> EditChain(ApprovalChainEditView model, CancellationToken cancellationToken)
		{
			if (!IsAdmin)
				return Unauthorized();
			var definition = new ApprovalChainDefinition
			{
				Chain = new ApprovalChain
				{
					ApprovalChainId = model.ApprovalChainId, DepartmentId = DepartmentId, SubjectKind = (int)model.Kind, Name = model.Name, IsActive = model.IsActive,
					AllowSelfApproval = model.AllowSelfApproval, ExpiresInDays = model.ExpiresInDays
				},
				Steps = (model.Steps ?? new List<ApprovalChainStepRow>()).Where(s => s != null && s.Include).Select(s => new ApprovalChainStep
				{
					DepartmentId = DepartmentId, StepOrder = s.StepOrder, ApproverKind = (int)s.ApproverKind, PersonnelRoleId = s.PersonnelRoleId, UserId = s.UserId,
					DepartmentGroupId = s.DepartmentGroupId, CertificationTypeId = s.CertificationTypeId, MinimumAmount = s.MinimumAmount, Label = s.Label,
					EscalateAfterHours = s.EscalateAfterHours, EscalateTo = (int)s.EscalateTo, EscalateToRoleId = s.EscalateToRoleId, EscalateToUserId = s.EscalateToUserId
				}).ToList()
			};
			var (refusal, _) = await _approvals.SaveChainAsync(definition, UserId, cancellationToken);
			if (refusal != ApprovalRefusals.None)
			{
				model.Error = RefusalText(refusal);
				await FillPickersAsync(model);
				return View(model);
			}
			TempData["ApprovalMessage"] = _text["ChainSavedMessage"].Value;
			return RedirectToAction("Chains");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeactivateChain(int id, CancellationToken cancellationToken)
		{
			if (!IsAdmin)
				return Unauthorized();
			await _approvals.DeactivateChainAsync(DepartmentId, id, UserId, cancellationToken);
			TempData["ApprovalMessage"] = _text["ChainDeactivatedMessage"].Value;
			return RedirectToAction("Chains");
		}

		private async Task FillPickersAsync(ApprovalChainEditView model)
		{
			model.HasAmount = ApprovalUses.HasAmount(model.Kind);
			while (model.Steps.Count < ApprovalChain.MaxSteps)
				model.Steps.Add(new ApprovalChainStepRow { StepOrder = model.Steps.Count + 1 });
			model.Roles = (await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).OrderBy(r => r.Name)
				.Select(r => new SelectOption { Value = r.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), Text = r.Name }).ToList();
			model.Members = await MemberOptionsAsync();
			model.Groups = (await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>()).OrderBy(g => g.Name)
				.Select(g => new SelectOption { Value = g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), Text = g.Name }).ToList();
			model.CertificationTypes = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>())
				.OrderBy(t => t.Name).Select(t => new SelectOption { Value = t.DepartmentCertificationTypeId.ToString(CultureInfo.InvariantCulture), Text = t.Name }).ToList();
		}

		private async Task<List<SelectOption>> MemberOptionsAsync() =>
			(await _departments.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
				.Select(p => new SelectOption { Value = p.UserId, Text = p.Name }).ToList();

		[HttpGet]
		public async Task<IActionResult> Delegates()
		{
			var all = await _approvals.GetDelegatesAsync(DepartmentId);
			return View(new ApprovalDelegatesView
			{
				Delegates = IsAdmin ? all : all.Where(d => ApprovalRouting.Same(d.UserId, UserId)).ToList(),
				Members = await MemberOptionsAsync(),
				Names = await NamesAsync(),
				IsAdmin = IsAdmin,
				CurrentUserId = UserId,
				Message = TempData["ApprovalMessage"] as string
			});
		}

		/// <summary>A member names their own delegate; a department admin may name anyone's.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveDelegate(string userId, string delegateUserId, DateTime? startsOn, DateTime? endsOn, CancellationToken cancellationToken)
		{
			var forUser = IsAdmin && !string.IsNullOrWhiteSpace(userId) ? userId : UserId;
			var saved = await _approvals.SaveDelegateAsync(DepartmentId, forUser, delegateUserId, startsOn?.ToUniversalTime(), endsOn?.ToUniversalTime(), UserId, cancellationToken);
			TempData["ApprovalMessage"] = saved != null ? _text["DelegateSavedMessage"].Value : _text["DelegateInvalidMessage"].Value;
			return RedirectToAction("Delegates");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveDelegate(int id, CancellationToken cancellationToken)
		{
			var existing = (await _approvals.GetDelegatesAsync(DepartmentId)).FirstOrDefault(d => d.ApprovalDelegateId == id);
			if (existing == null || (!IsAdmin && !ApprovalRouting.Same(existing.UserId, UserId)))
				return Unauthorized();
			await _approvals.RemoveDelegateAsync(DepartmentId, id, UserId, cancellationToken);
			TempData["ApprovalMessage"] = _text["DelegateRemovedMessage"].Value;
			return RedirectToAction("Delegates");
		}

		#endregion

		#region Mutual-aid agreements (department admins)

		[HttpGet]
		public async Task<IActionResult> Agreements()
		{
			if (!IsAdmin)
				return Unauthorized();
			var view = new MutualAidAgreementsView { Agreements = await _agreements.GetAgreementsAsync(DepartmentId), Message = TempData["ApprovalMessage"] as string };
			foreach (var agreement in view.Agreements)
			{
				var status = await _approvals.GetSubjectStatusAsync(DepartmentId, SignatureSubjectKinds.MutualAidAgreementApproval,
					agreement.MutualAidAgreementId.ToString(CultureInfo.InvariantCulture), UserId, true);
				if (status.Latest != null)
					view.Latest[agreement.MutualAidAgreementId] = status.Latest;
			}
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> EditAgreement(int id = 0)
		{
			if (!IsAdmin)
				return Unauthorized();
			var model = new MutualAidAgreementEditView();
			if (id > 0)
			{
				var agreement = await _agreements.GetAgreementAsync(DepartmentId, id);
				if (agreement == null)
					return NotFound();
				model.MutualAidAgreementId = agreement.MutualAidAgreementId;
				model.Title = agreement.Title;
				model.DepartmentLinkId = agreement.DepartmentLinkId;
				model.PartnerName = agreement.PartnerName;
				model.Terms = agreement.Terms;
				model.EffectiveOn = agreement.EffectiveOn;
				model.ExpiresOn = agreement.ExpiresOn;
				model.Status = agreement.Status;
				model.Approval = await _approvals.GetSubjectStatusAsync(DepartmentId, SignatureSubjectKinds.MutualAidAgreementApproval, id.ToString(CultureInfo.InvariantCulture), UserId, true);
				// Round 10 A3: the partner's countersignature over the link (a declined, expired or voided request is recorded on read).
				agreement = await _countersign.RefreshAsync(agreement, HttpContext?.RequestAborted ?? default);
				model.RequiresCountersignature = agreement.RequiresCountersignature;
				model.CountersignStatus = agreement.CountersignStatus;
				model.CountersignRequestedOn = agreement.CountersignRequestedOn;
				model.CountersignedOn = agreement.CountersignedOn;
				model.CountersignNote = agreement.CountersignNote;
			}
			model.Links = await LinkOptionsAsync();
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> EditAgreement(MutualAidAgreementEditView model, CancellationToken cancellationToken)
		{
			if (!IsAdmin)
				return Unauthorized();
			var links = await _links.GetAllLinksForDepartmentAsync(DepartmentId) ?? new List<DepartmentLink>();
			var link = model.DepartmentLinkId > 0 ? links.FirstOrDefault(l => l.DepartmentLinkId == model.DepartmentLinkId.Value) : null;
			var saved = await _agreements.SaveAsync(new MutualAidAgreement
			{
				MutualAidAgreementId = model.MutualAidAgreementId,
				DepartmentId = DepartmentId,
				Title = model.Title,
				DepartmentLinkId = link?.DepartmentLinkId,
				PartnerDepartmentId = link == null ? null : (link.DepartmentId == DepartmentId ? link.LinkedDepartmentId : link.DepartmentId),
				PartnerName = string.IsNullOrWhiteSpace(model.PartnerName) ? (link == null ? null : (link.DepartmentId == DepartmentId ? link.LinkedDepartment?.Name : link.Department?.Name)) : model.PartnerName,
				Terms = model.Terms,
				EffectiveOn = model.EffectiveOn,
				ExpiresOn = model.ExpiresOn,
				RequiresCountersignature = model.RequiresCountersignature && link != null
			}, UserId, cancellationToken);
			if (saved == null)
			{
				model.Error = _text["AgreementInvalidMessage"].Value;
				model.Links = await LinkOptionsAsync();
				return View(model);
			}
			TempData["ApprovalMessage"] = _text["AgreementSavedMessage"].Value;
			return RedirectToAction("EditAgreement", new { id = saved.MutualAidAgreementId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteAgreement(int id, CancellationToken cancellationToken)
		{
			if (!IsAdmin)
				return Unauthorized();
			var deleted = await _agreements.DeleteAsync(DepartmentId, id, UserId, cancellationToken);
			TempData["ApprovalMessage"] = deleted ? _text["AgreementDeletedMessage"].Value : _text["AgreementInvalidMessage"].Value;
			return RedirectToAction("Agreements");
		}

		private async Task<List<SelectOption>> LinkOptionsAsync()
		{
			var options = new List<SelectOption>();
			foreach (var link in await _links.GetAllLinksForDepartmentAsync(DepartmentId) ?? new List<DepartmentLink>())
			{
				var partner = link.DepartmentId == DepartmentId ? link.LinkedDepartment?.Name : link.Department?.Name;
				options.Add(new SelectOption { Value = link.DepartmentLinkId.ToString(CultureInfo.InvariantCulture), Text = partner ?? ("#" + link.DepartmentLinkId.ToString(CultureInfo.InvariantCulture)) });
			}
			return options;
		}

		#endregion

		/// <summary>Where a subject is shown in its module, when it has a page.</summary>
		private string SubjectUrl(SignatureSubjectKinds kind, string subjectId) => kind switch
		{
			SignatureSubjectKinds.InvoiceApproval => Url.Action("View", "Invoicing", new { area = "User", id = subjectId }),
			SignatureSubjectKinds.CalOesMarsPackageApproval => Url.Action("WorkItem", "CalOesMars", new { area = "User", id = subjectId }),
			SignatureSubjectKinds.MutualAidAgreementApproval => Url.Action("EditAgreement", "Approvals", new { area = "User", id = subjectId }),
			_ => null
		};
	}
}

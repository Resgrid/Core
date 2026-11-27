using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Signatures
{
	/// <summary>"My signatures" (S3): the member's steps whose turn it is.</summary>
	public class MySignaturesView
	{
		public List<PendingSignatureItem> Items { get; set; } = new List<PendingSignatureItem>();
		public string Message { get; set; }
	}

	/// <summary>One multi-signer request as a member sees it (S3): their step's panel, every step and the actions open to them.</summary>
	public class SignatureStepPageView
	{
		public SignatureStepView Step { get; set; }
		public IReadOnlyDictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
		public SignaturePanelModel Panel { get; set; }
		/// <summary>The member may act on the subject in its module (withdraw the request, see its details).</summary>
		public bool CanManage { get; set; }
		public bool CanDecline { get; set; }
		public string SubjectUrl { get; set; }
		public string ReturnUrl { get; set; }
		public string Message { get; set; }
	}

	/// <summary>A subject's signing page (S3, issue receipts): what is signed and the shared external-signer panel.</summary>
	public class SignatureSubjectPageView
	{
		public SignatureSubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public SignatureSubjectInfo Info { get; set; }
		public bool CanRequest { get; set; }
		public string ReturnUrl { get; set; }
		/// <summary>A member who can sign the subject themselves (the issue receipt's recipient).</summary>
		public string MemberSignerUserId { get; set; }
		public string MemberSignerName { get; set; }
		public SignatureSignerRoles MemberSignerRole { get; set; } = SignatureSignerRoles.Recipient;
	}

	/// <summary>The checklist witness signature on a completion page (S3).</summary>
	public class ChecklistWitnessPanelModel
	{
		public Resgrid.Model.Checklists.ChecklistWitnessSignatureView View { get; set; }
		/// <summary>The shared signature panel while the run awaits its witness (null afterwards).</summary>
		public SignaturePanelModel Panel { get; set; }
		public string Message { get; set; }
	}

	/// <summary>The shared pieces of the step panels (Step page and the module pages' signature panel).</summary>
	public static class SignatureStepPresentation
	{
		/// <summary>"Shift officer · Jane Doe", "Evaluator · any eligible member", "Vendor · Acme Ltd" (role text when no label).</summary>
		public static string Describe(SignatureRequestSigner signer, IReadOnlyDictionary<string, string> names, IStringLocalizer text)
		{
			if (signer == null)
				return string.Empty;
			var role = !string.IsNullOrWhiteSpace(signer.StepLabel) ? signer.StepLabel : text["Role" + (SignatureSignerRoles)signer.Role].Value;
			string who;
			if (signer.IsMember)
				who = !string.IsNullOrWhiteSpace(signer.UserId) ? (names != null && names.TryGetValue(signer.UserId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : text["MemberFallbackName"].Value)
					: text["AnyEligibleMember"].Value;
			else
				who = string.IsNullOrWhiteSpace(signer.ExternalName) ? text["OutsideSignerFallbackName"].Value : signer.ExternalNameForDisplay;
			return role + " · " + who;
		}

		/// <summary>The level badge text of a step (L1/L2 member, L4 external).</summary>
		public static int Level(SignatureRequestSigner signer) => (int)SignatureOrdering.StepLevel(signer);

		/// <summary>The shared signature panel for the member's step (sign, or why not now).</summary>
		public static SignaturePanelModel Panel(SignatureStepView view, IReadOnlyDictionary<string, string> names, IUrlHelper url, IStringLocalizer text, string returnUrl,
			bool includePasskey)
		{
			var step = view.MyStep ?? view.MyWaitingStep;
			var level = view.MyLevel;
			var panel = new SignaturePanelModel
			{
				Title = text["StepPanelTitle"].Value,
				Statement = view.Request.StatementText,
				ArtifactSha256 = view.Request.ArtifactSha256,
				RequiredLevel = (int)level,
				OthersToSign = view.OthersToSign.Select(s => Describe(s, names, text)).ToList(),
				Note = step == null ? null : string.Format(CultureInfo.CurrentCulture, text["YourStepNote"].Value, Describe(step, names, text)),
				SignAction = level >= SignatureLevels.StepUp ? url.Action("SignStepUp", "Signatures", new { area = "User" }) : url.Action("SignStep", "Signatures", new { area = "User" }),
				PasskeyAction = includePasskey && level >= SignatureLevels.StepUp ? url.Action("SignStepWithPasskey", "Signatures", new { area = "User" }) : null,
				PasskeyOptionsUrl = includePasskey && level >= SignatureLevels.StepUp ? url.Action("StepPasskeyOptions", "Signatures", new { area = "User" }) : null,
				Fields = new Dictionary<string, string>
				{
					["purpose"] = "step",
					["id"] = view.Request.SignatureRequestId.ToString(CultureInfo.InvariantCulture),
					["signerId"] = step?.SignatureRequestSignerId.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
					["returnUrl"] = returnUrl ?? string.Empty
				},
				SubmitLabel = text["SignStepButton"].Value,
				Disabled = view.MyStep == null,
				DisabledReason = view.MyStep == null ? text["Refusal" + view.Blocked].Value : null
			};
			return panel;
		}

		/// <summary>Where a subject is shown in its module, when it has a page.</summary>
		public static string SubjectUrl(IUrlHelper url, SignatureSubjectKinds kind, string subjectId) => kind switch
		{
			SignatureSubjectKinds.WorkOrderVendorAcceptance => url.Action("Detail", "WorkOrders", new { area = "User", id = subjectId }),
			SignatureSubjectKinds.InventoryIssueReceipt => url.Action("Subject", "Signatures", new { area = "User", kind = (int)kind, subjectId }),
			SignatureSubjectKinds.TimeReport => url.Action("TimeReport", "Deployments", new { area = "User", id = subjectId }),
			SignatureSubjectKinds.Bid => url.Action("View", "Bids", new { area = "User", id = subjectId }),
			SignatureSubjectKinds.ServiceContract => url.Action("View", "Contracts", new { area = "User", id = subjectId }),
			_ => null
		};
	}
}

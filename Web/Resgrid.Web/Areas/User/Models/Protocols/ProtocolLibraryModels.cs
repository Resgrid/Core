using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Signatures;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	/// <summary>The protocol library (Protocols/Index, P1 §18.2).</summary>
	public class ProtocolLibraryModel
	{
		public List<ProtocolLibraryRow> Rows { get; set; } = new List<ProtocolLibraryRow>();
		public List<ProtocolLibraryRow> Favorites { get; set; } = new List<ProtocolLibraryRow>();
		public List<ProtocolLibraryRow> Recent { get; set; } = new List<ProtocolLibraryRow>();
		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		public Dictionary<int, int> CategoryCounts { get; set; } = new Dictionary<int, int>();
		public Dictionary<string, string> Owners { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public int Total { get; set; }
		public int PublishedCount { get; set; }
		public int InReviewCount { get; set; }
		public int DueForReviewCount { get; set; }
		/// <summary>Distinct protocols used on at least one call logged in the last 30 days.</summary>
		public int UsedOnCallsCount { get; set; }

		public string Query { get; set; }
		public int? Kind { get; set; }
		public int? CategoryId { get; set; }
		/// <summary>"", published, draft, review, retired or disabled.</summary>
		public string Status { get; set; }
		public string Owner { get; set; }
		/// <summary>"", due (within 30 days or overdue) or overdue.</summary>
		public string Review { get; set; }
		/// <summary>"", everyone or restricted.</summary>
		public string Audience { get; set; }
		public bool HasFilter => !string.IsNullOrEmpty(Query) || Kind.HasValue || CategoryId.HasValue || !string.IsNullOrEmpty(Status) ||
			!string.IsNullOrEmpty(Owner) || !string.IsNullOrEmpty(Review) || !string.IsNullOrEmpty(Audience);

		public bool CanCreate { get; set; }
		public bool CanApprove { get; set; }
		public bool IsManager { get; set; }
		public bool IsAdmin { get; set; }
		public bool CanDelete { get; set; }
		public string Message { get; set; }
	}

	public class ProtocolLibraryRow
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Current { get; set; }
		/// <summary>The open working copy (draft, in review or approved), if any.</summary>
		public ProtocolVersion Open { get; set; }
		public string CategoryName { get; set; }
		public string OwnerName { get; set; }
		public int CallsLast30Days { get; set; }
		public bool IsFavorite { get; set; }
		public bool HasAudience { get; set; }
		public List<string> Triggers { get; set; } = new List<string>();
		/// <summary>The pill: published, draft, review, approved, scheduled, retired, disabled or deleted.</summary>
		public string StatusKey { get; set; }
		public bool ReviewOverdue { get; set; }
		public bool ReviewDueSoon { get; set; }
	}

	/// <summary>The protocol detail page (Protocols/View, P1).</summary>
	public class ProtocolDetailModel
	{
		public DispatchProtocol Protocol { get; set; }
		/// <summary>The version on screen (the one in force unless another was picked).</summary>
		public ProtocolVersion Shown { get; set; }
		public ProtocolVersion Current { get; set; }
		public ProtocolVersion Open { get; set; }
		public List<ProtocolVersion> Versions { get; set; } = new List<ProtocolVersion>();
		public List<ProtocolContentAnchor> Outline { get; set; } = new List<ProtocolContentAnchor>();
		public List<string> TriggerLines { get; set; } = new List<string>();
		public List<string> AudienceLabels { get; set; } = new List<string>();
		public int CallsLast30Days { get; set; }
		public List<ProtocolCallUsage> RecentCalls { get; set; } = new List<ProtocolCallUsage>();
		public Dictionary<int, int> CallsByVersion { get; set; } = new Dictionary<int, int>();
		public SignatureListModel Signatures { get; set; } = new SignatureListModel();
		public List<ProtocolComment> Threads { get; set; } = new List<ProtocolComment>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string CategoryName { get; set; }
		public string StatusKey { get; set; }
		public bool IsFavorite { get; set; }
		public bool IsManager { get; set; }
		public bool CanAuthor { get; set; }
		public bool CanApprove { get; set; }
		public bool CanModify { get; set; }
		public bool CanDelete { get; set; }
		public bool CanRecordReview { get; set; }
		public SignaturePanelModel ReviewPanel { get; set; }
		/// <summary>Shown as a member would see it ("preview as member").</summary>
		public bool Preview { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The approver's review of one version: the redline first, then comments, steps and the signature panel.</summary>
	public class ProtocolReviewModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public ProtocolVersion Current { get; set; }
		public ProtocolDiffResult Diff { get; set; }
		public List<ProtocolVersionApproval> Steps { get; set; } = new List<ProtocolVersionApproval>();
		public List<ProtocolComment> Comments { get; set; } = new List<ProtocolComment>();
		public List<ProtocolContentAnchor> Outline { get; set; } = new List<ProtocolContentAnchor>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<int, string> Roles { get; set; } = new Dictionary<int, string>();
		public int OpenComments { get; set; }
		public bool CanDecide { get; set; }
		public bool CanPublish { get; set; }
		public bool CanWithdraw { get; set; }
		public SignaturePanelModel Panel { get; set; }
		public SignatureListModel Signatures { get; set; } = new SignatureListModel();
		public string Message { get; set; }
		public string Error { get; set; }
		public string TimeZoneName { get; set; }
	}

	public class ProtocolCompareModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion From { get; set; }
		public ProtocolVersion To { get; set; }
		public List<ProtocolVersion> Versions { get; set; } = new List<ProtocolVersion>();
		public ProtocolDiffResult Diff { get; set; }
	}

	public class ProtocolReviewQueueModel
	{
		public List<ProtocolReviewItem> Items { get; set; } = new List<ProtocolReviewItem>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<int, string> Roles { get; set; } = new Dictionary<int, string>();
	}

	public class ProtocolCategoriesModel
	{
		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		public Dictionary<int, int> Counts { get; set; } = new Dictionary<int, int>();
		public Dictionary<int, string> Roles { get; set; } = new Dictionary<int, string>();
		public Dictionary<string, string> Members { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>A category save. Approval steps come as "u:{userId}" or "r:{roleId}" values, in order.</summary>
	public class ProtocolCategoryInput
	{
		public int ProtocolCategoryId { get; set; }
		public string Name { get; set; }
		public int? ParentCategoryId { get; set; }
		public int? Kind { get; set; }
		public int SortOrder { get; set; }
		public List<string> Approvers { get; set; } = new List<string>();
	}

	public class ProtocolSettingsModel
	{
		public bool RequirePasskeyForApproval { get; set; }
		public bool ReadAndSignRequiresStepUp { get; set; }
		public int DefaultReviewIntervalDays { get; set; }
		public bool LegalHold { get; set; }
		/// <summary>P3: the language protocols are written in (the translation fallback); blank = English.</summary>
		public string DefaultContentLocale { get; set; }
		/// <summary>P4: the determinant and protocol name in the dispatch push, text message and voice prompt (off by default).</summary>
		public bool ShowDeterminantInDispatch { get; set; }
		/// <summary>Keep-days per kind (index = ProtocolKinds value); 0 keeps every version.</summary>
		public List<int> RetentionDays { get; set; } = new List<int>();
		[BindNever] public string Message { get; set; }
	}

	public class ProtocolImportModel
	{
		public ProtocolImportResult Result { get; set; }
	}

	/// <summary>The P1 additions to the editor (shared by the creation wizard and the full-page editor).</summary>
	public class ProtocolLibraryEditorFields
	{
		public int Kind { get; set; }
		public int? CategoryId { get; set; }
		public string Number { get; set; }
		public string Tags { get; set; }
		public string OwnerUserId { get; set; }
		public int? ReviewIntervalDays { get; set; }
		public bool ExcludeFromAi { get; set; }
		public bool IsLicensedContent { get; set; }
		public bool LegalHold { get; set; }
		public bool RequiresAcknowledgment { get; set; }
		public string ChangeSummary { get; set; }
		/// <summary>Everyone, or the picked groups, roles, shifts, unit types and certification types.</summary>
		public bool AudienceEveryone { get; set; } = true;
		public List<int> AudienceGroups { get; set; } = new List<int>();
		public List<int> AudienceRoles { get; set; } = new List<int>();
		public List<int> AudienceShifts { get; set; } = new List<int>();
		public List<int> AudienceUnitTypes { get; set; } = new List<int>();
		public List<int> AudienceCertificationTypes { get; set; } = new List<int>();
	}

	/// <summary>Lists the library editor's pickers need.</summary>
	public class ProtocolEditorLists
	{
		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		public Dictionary<string, string> Members { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<int, string> Groups { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> Roles { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> Shifts { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> UnitTypes { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> CertificationTypes { get; set; } = new Dictionary<int, string>();
	}
}

namespace Resgrid.Web.Areas.User.Models.Signatures
{
	/// <summary>
	/// The shared signature panel (S0, plan §23 UX): the exact statement, the content hash short code, the level badge, who else
	/// must sign, and the actions "Sign" (step-up) and "Sign with passkey" (a passkey assertion bound to the hash).
	/// </summary>
	public class SignaturePanelModel
	{
		public string Title { get; set; }
		public string Statement { get; set; }
		public string ArtifactSha256 { get; set; }
		public string ShortCode => SignaturePolicy.ShortCode(ArtifactSha256);
		public int RequiredLevel { get; set; } = (int)SignatureLevels.StepUp;
		public bool RequirePasskey { get; set; }
		/// <summary>Who else must sign after this signer (names or roles).</summary>
		public List<string> OthersToSign { get; set; } = new List<string>();
		/// <summary>The step-up form's action (the action requires a recent step-up and replays the form after it).</summary>
		public string SignAction { get; set; }
		/// <summary>The passkey form's action; null hides "Sign with passkey".</summary>
		public string PasskeyAction { get; set; }
		/// <summary>Where the passkey options bound to this content are fetched from (POST).</summary>
		public string PasskeyOptionsUrl { get; set; }
		/// <summary>Extra hidden fields both forms post (ids).</summary>
		public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
		/// <summary>Shown a comment box (approvals); its field name.</summary>
		public string CommentField { get; set; }
		public string SubmitLabel { get; set; }
		public string Note { get; set; }
		public bool Disabled { get; set; }
		public string DisabledReason { get; set; }
		/// <summary>S2: "Hand device to signer" posts here (an in-person L4 request); null hides it.</summary>
		public string HandDeviceAction { get; set; }
		/// <summary>Hidden fields the hand-device form posts.</summary>
		public Dictionary<string, string> HandDeviceFields { get; set; } = new Dictionary<string, string>();
		/// <summary>S1/S2: the "Signed" state replaces the actions; it links the sealed PDF when there is one.</summary>
		public bool Signed { get; set; }
		public string SealedPdfUrl { get; set; }
		/// <summary>"Signed by … on …" or "Signed, sealing pending".</summary>
		public string SignedNote { get; set; }
	}

	/// <summary>A subject's signatures with their level and whether they still match the artifact.</summary>
	public class SignatureListModel
	{
		public List<SignatureVerification> Items { get; set; } = new List<SignatureVerification>();
		public string EmptyText { get; set; }
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Signatures
{
	/// <summary>The external-signer panel a module page shows for one subject (S2): request buttons, status and signatures.</summary>
	public class SignatureRequestPanelModel
	{
		public SignatureSubjectKinds Kind { get; set; }
		public string SubjectId { get; set; }
		public string ReturnUrl { get; set; }
		/// <summary>The member may request signatures on this subject (the module's own rule, re-checked on POST).</summary>
		public bool CanRequest { get; set; }
		public SubjectSignatureSummary Summary { get; set; } = new SubjectSignatureSummary();
		public string SuggestedName { get; set; }
		public string SuggestedEmail { get; set; }
		/// <summary>The shared signature panel (S0) in its in-person / signed states.</summary>
		public SignaturePanelModel Panel { get; set; }
		public bool SealingConfigured { get; set; }
		/// <summary>S3: the member's own step in the open request (null when they have none).</summary>
		public SignaturePanelModel StepPanel { get; set; }
		/// <summary>S3: the steps of the request shown (open, else the latest completed), in order.</summary>
		public List<SignatureRequestSigner> Steps { get; set; } = new List<SignatureRequestSigner>();
		public IReadOnlyDictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
		/// <summary>S3: a member the module names to sign in Resgrid (the issue receipt's recipient).</summary>
		public string MemberSignerUserId { get; set; }
		public string MemberSignerName { get; set; }
		public SignatureSignerRoles MemberSignerRole { get; set; } = SignatureSignerRoles.Member;

		public SignatureRequestRecord Open => Summary?.Open;
		public SignatureRequestRecord Completed => Summary?.LatestCompleted;

		public SignatureRequestSigner SignerOf(SignatureRequestRecord request) =>
			request == null ? null : Summary?.Signers?.Where(s => s.SignatureRequestId == request.SignatureRequestId).OrderBy(s => s.SignerOrder).FirstOrDefault();
	}

	/// <summary>"Hand device to signer": the capture page on a member's device.</summary>
	public class HandDeviceView
	{
		public SignatureRequestRecord Request { get; set; }
		public string DepartmentName { get; set; }
		public string ReturnUrl { get; set; }
		public string ConsentText { get; set; }
		public string ConsentVersion { get; set; }
		public string Message { get; set; }
		public string PrintedName { get; set; }
	}

	/// <summary>"Verify a document" (S1): the upload and what DSS and Resgrid's own records say about it.</summary>
	public class VerifyDocumentView
	{
		public bool Submitted { get; set; }
		public string FileName { get; set; }
		public DocumentVerificationResult Result { get; set; }
		public SealingAvailability Availability { get; set; }
		public string Message { get; set; }
	}

	/// <summary>The department's e-sign platform account (Documenso team id and token).</summary>
	public class ESignSettingsView
	{
		public bool PlatformConfigured { get; set; }
		public SealingAvailability Sealing { get; set; }
		public string TeamId { get; set; }
		public string TokenHint { get; set; }
		public bool HasToken { get; set; }
		public bool IsActive { get; set; } = true;
		public bool Saved { get; set; }
		public string Message { get; set; }
	}

	/// <summary>A request's detail page (status, signers with masked contacts, documents).</summary>
	public class SignatureRequestDetailView
	{
		public SignatureRequestRecord Request { get; set; }
		public List<SignatureRequestSigner> Signers { get; set; } = new List<SignatureRequestSigner>();
		public SealedDocument Sealed { get; set; }
		public SealedDocument ProviderCompleted { get; set; }
		public string ReturnUrl { get; set; }
		public string Message { get; set; }
		/// <summary>Shown once when the link could not be delivered: the requester can pass it on.</summary>
		public string LinkUrl { get; set; }
		/// <summary>S3: member signers' display names by user id.</summary>
		public IReadOnlyDictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
	}
}

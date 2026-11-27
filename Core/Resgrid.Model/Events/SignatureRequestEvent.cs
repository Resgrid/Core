using System;

namespace Resgrid.Model.Events
{
	/// <summary>What happened to an external-signer request (<see cref="SignatureRequestEvent"/>).</summary>
	public enum SignatureRequestChanges
	{
		Requested = 1,
		Completed = 2,
		Declined = 3,
		Expired = 4
	}

	/// <summary>
	/// An external-signer (L4) request was sent, completed, declined or expired (S2). Workflow triggers 220-223. The payload names
	/// the subject and the counts only: it never carries the signer's name, e-mail address or phone number, so a department's
	/// workflow (e.g. "time report signed by the customer → generate the invoice line") cannot leak an outside person's contact.
	/// </summary>
	public class SignatureRequestEvent
	{
		public int DepartmentId { get; set; }

		public SignatureRequestChanges Change { get; set; }

		public int SignatureRequestId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/> as its name ("TimeReport").</summary>
		public string SubjectKind { get; set; }

		public string SubjectId { get; set; }

		public string Title { get; set; }

		/// <summary>"Remote" or "InPerson".</summary>
		public string Channel { get; set; }

		/// <summary><see cref="SignatureRequestStatuses"/> as its name.</summary>
		public string Status { get; set; }

		public int SignerCount { get; set; }

		public int SignedCount { get; set; }

		/// <summary>The short content code the signatures bind to ("3F2A-91C0").</summary>
		public string ContentCode { get; set; }

		public DateTime ExpiresOn { get; set; }

		public DateTime OccurredOn { get; set; }

		/// <summary>The sealed evidence document, when the request completed and sealing ran.</summary>
		public int? SealedDocumentId { get; set; }

		/// <summary>The member who requested it, or null when the system (worker 90) recorded the change.</summary>
		public string UserId { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case SignatureRequestChanges.Completed: return WorkflowTriggerEventType.SignatureCompleted;
					case SignatureRequestChanges.Declined: return WorkflowTriggerEventType.SignatureDeclined;
					case SignatureRequestChanges.Expired: return WorkflowTriggerEventType.SignatureExpired;
					default: return WorkflowTriggerEventType.SignatureRequested;
				}
			}
		}
	}
}

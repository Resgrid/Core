using System;

namespace Resgrid.Model.Events
{
	/// <summary>What happened to an approval request (<see cref="ApprovalWorkflowEvent"/>).</summary>
	public enum ApprovalWorkflowChanges
	{
		Requested = 1,
		Granted = 2,
		Rejected = 3,
		Escalated = 4
	}

	/// <summary>
	/// An approval was requested, granted, rejected or escalated (e-Signature S4; workflow triggers 236-239), so a department can
	/// automate it ("purchase over 5,000 approved → notify finance"). The payload carries ids, the subject's title, amount and the
	/// step label only: never a member's name, e-mail address or phone, nor an outside party's (the workflow resolves members from the
	/// ids under its own rules).
	/// </summary>
	public class ApprovalWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public ApprovalWorkflowChanges Change { get; set; }

		public int ApprovalRequestId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/> as its name ("InvoiceApproval").</summary>
		public string SubjectKind { get; set; }

		public string SubjectId { get; set; }

		public string Title { get; set; }

		public decimal? Amount { get; set; }

		public string Currency { get; set; }

		/// <summary>The member who submitted it.</summary>
		public string SubmittedByUserId { get; set; }

		/// <summary>The member who acted (the approver, the rejecter); null for the worker.</summary>
		public string UserId { get; set; }

		/// <summary>The step that escalated (escalation only).</summary>
		public string StepLabel { get; set; }

		/// <summary>The rejection reason the approver gave (rejection only).</summary>
		public string Reason { get; set; }

		/// <summary>The short content code the approvals bind to.</summary>
		public string ContentCode { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case ApprovalWorkflowChanges.Granted: return WorkflowTriggerEventType.ApprovalGranted;
					case ApprovalWorkflowChanges.Rejected: return WorkflowTriggerEventType.ApprovalRejected;
					case ApprovalWorkflowChanges.Escalated: return WorkflowTriggerEventType.ApprovalEscalated;
					default: return WorkflowTriggerEventType.ApprovalRequested;
				}
			}
		}
	}
}

using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>
	/// costRecovery.queue (plan §7.9): the Cal OES MARS action queue, scoped as the Queue page scopes it — managers
	/// (ManageMutualAidReimbursement / admin) the agency's queue plus the readiness check, rostered members their own drafts.
	/// </summary>
	public sealed class CostRecoveryQueueWidgetData
	{
		/// <summary>The viewer works the agency queue (manager); false = their own items only, and Readiness is null.</summary>
		public bool ManagerScope { get; set; }

		public int Total { get; set; }

		public int Mine { get; set; }

		/// <summary>Items with validation errors.</summary>
		public int WithErrors { get; set; }

		/// <summary>Oldest first (age days descending); at most the maxRows setting.</summary>
		public List<CostRecoveryQueueRow> Rows { get; set; } = new List<CostRecoveryQueueRow>();

		/// <summary>Agency readiness (Index page); managers only.</summary>
		public CostRecoveryReadiness Readiness { get; set; }
	}

	public sealed class CostRecoveryQueueRow
	{
		public string WorkItemId { get; set; }

		/// <summary>CalOesMarsRecordTypes name (F42, ExpenseClaim…) — W.costRecovery.queue.RecordType.{name}.</summary>
		public string RecordType { get; set; }

		/// <summary>CalOesMarsLocalStates name (Draft, NeedsReview…) — W.costRecovery.queue.State.{name}.</summary>
		public string LocalState { get; set; }

		public string DeploymentName { get; set; }

		public string IncidentNumber { get; set; }

		public string RequestNumber { get; set; }

		public int ErrorCount { get; set; }

		public int WarningCount { get; set; }

		public int AgeDays { get; set; }

		public bool IsMine { get; set; }
	}

	public sealed class CostRecoveryReadiness
	{
		public bool IsReady { get; set; }

		public int Blockers { get; set; }

		public int Warnings { get; set; }

		public bool AuthorityProfileCurrent { get; set; }

		public int OpenWorkItems { get; set; }

		public int ReturnedWorkItems { get; set; }

		public int InvoicesAwaitingLocalApproval { get; set; }
	}
}

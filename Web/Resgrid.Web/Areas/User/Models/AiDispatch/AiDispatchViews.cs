using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;

namespace Resgrid.Web.Areas.User.Models.AiDispatch
{
	/// <summary>AI dispatch settings page (enhanced-ai-addon-plan.md §4). Confidence is edited as a whole percentage.</summary>
	public sealed class AiDispatchSettingsView
	{
		public AiDispatchStatus Status { get; set; }
		public int? MinimumConfidencePercent { get; set; }
		public string SenderAllowlist { get; set; }
		public int? MonthlyTokenCap { get; set; }
		public int AuditRetentionDays { get; set; } = AiDispatchSettingsPolicy.DefaultRetentionDays;
		public bool FillCallType { get; set; } = true;
		public bool FillAddress { get; set; } = true;
		public bool FillContact { get; set; } = true;
		public bool FillIncidentNumber { get; set; } = true;
		public bool RenamePlaceholder { get; set; } = true;
		public bool AddSummaryNote { get; set; } = true;
		public bool FlagRelatedCalls { get; set; } = true;
		public long Revision { get; set; }
		public long MonthlyUsage { get; set; }
		public int DefaultConfidencePercent { get; set; }
		public DateTime? UpdatedOnLocal { get; set; }
		public List<string> Errors { get; set; } = new List<string>();
		public bool Saved { get; set; }
	}

	/// <summary>AI dispatch activity viewer: metadata-only audit rows, newest first.</summary>
	public sealed class AiDispatchActivityView
	{
		public AiDispatchStatus Status { get; set; }
		public Department Department { get; set; }
		public List<AiDispatchAuditListItem> Items { get; set; } = new List<AiDispatchAuditListItem>();
		public int Take { get; set; }
		public int EnrichedLast30Days { get; set; }
		public int SkippedLast30Days { get; set; }
		public long MonthlyUsage { get; set; }
		public int? MonthlyTokenCap { get; set; }
	}
}

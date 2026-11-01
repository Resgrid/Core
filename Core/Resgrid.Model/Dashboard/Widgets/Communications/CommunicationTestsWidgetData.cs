using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Communications
{
	/// <summary>
	/// comms.tests (plan §7.5): recent communication test runs (the Communication Tests page list) and, for department
	/// administrators, delivery and response by channel for the latest run (group admins see per-member results only for
	/// their own groups on the report page, so the department-wide breakdown is not theirs).
	/// </summary>
	public sealed class CommunicationTestsWidgetData
	{
		public List<CommunicationTestRunItem> Runs { get; set; } = new List<CommunicationTestRunItem>();

		/// <summary>True when <see cref="LatestRunChannels"/> was computed (department admins).</summary>
		public bool ChannelsVisible { get; set; }

		public Guid? LatestRunId { get; set; }

		public List<CommunicationTestChannelItem> LatestRunChannels { get; set; } = new List<CommunicationTestChannelItem>();
	}

	public sealed class CommunicationTestRunItem
	{
		public Guid CommunicationTestRunId { get; set; }

		public string TestName { get; set; }

		public DateTime StartedOnUtc { get; set; }

		public DateTime? CompletedOnUtc { get; set; }

		/// <summary>CommunicationTestRunStatus value.</summary>
		public int Status { get; set; }

		/// <summary>"pending", "running", "awaitingResponses", "completed", "failed".</summary>
		public string StatusKey { get; set; }

		public int TotalUsersTested { get; set; }

		public int TotalResponses { get; set; }

		/// <summary>Responses / users tested, 0–100 (null when nobody was tested).</summary>
		public int? ResponseRate { get; set; }

		public string Url { get; set; }
	}

	public sealed class CommunicationTestChannelItem
	{
		/// <summary>CommunicationTestChannel value (0 SMS, 1 email, 2 voice, 3 push).</summary>
		public int Channel { get; set; }

		/// <summary>"sms", "email", "voice", "push".</summary>
		public string ChannelKey { get; set; }

		/// <summary>Result rows for the channel (members it applied to).</summary>
		public int Targeted { get; set; }

		public int Suppressed { get; set; }

		public int Attempted { get; set; }

		public int Delivered { get; set; }

		public int Responded { get; set; }

		/// <summary>Delivered / attempted, 0–100 (null when nothing was attempted).</summary>
		public int? DeliveryRate { get; set; }

		/// <summary>Responded / delivered, 0–100 (null when nothing was delivered).</summary>
		public int? ResponseRate { get; set; }
	}
}

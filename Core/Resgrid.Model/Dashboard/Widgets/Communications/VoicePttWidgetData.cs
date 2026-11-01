using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Communications
{
	/// <summary>voice.ptt (plan §7.5): the department's Push-To-Talk channels and current seat utilization.</summary>
	public sealed class VoicePttWidgetData
	{
		/// <summary>False when the seat/participant read failed (billing or LiveKit unavailable); the channel list still shows.</summary>
		public bool UtilizationAvailable { get; set; }

		/// <summary>Seats the add-on pays for (10 per add-on quantity).</summary>
		public int SeatLimit { get; set; }

		/// <summary>Participants currently connected across the department's channels.</summary>
		public int CurrentlyActive { get; set; }

		public List<VoicePttChannel> Channels { get; set; } = new List<VoicePttChannel>();
	}

	public sealed class VoicePttChannel
	{
		public string DepartmentVoiceChannelId { get; set; }

		public string Name { get; set; }

		public bool IsDefault { get; set; }

		/// <summary>An on-demand channel opened for a call.</summary>
		public bool IsOnDemand { get; set; }

		public int? CallId { get; set; }

		public int ConferenceNumber { get; set; }
	}
}

using System;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.crr (plan §7.8): community risk reduction year to date (department-local year) and open alarm requests.</summary>
	public sealed class RecordsCrrWidgetData
	{
		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		public int Activities { get; set; }

		public int Audience { get; set; }

		public int SmokeAlarmsInstalled { get; set; }

		public int CoAlarmsInstalled { get; set; }

		public int ComboAlarmsInstalled { get; set; }

		public int AlarmsInstalled { get; set; }

		/// <summary>Homes or sites that received at least one alarm.</summary>
		public int AlarmVisits { get; set; }

		/// <summary>Alarm requests open now (not limited to the year).</summary>
		public int OpenAlarmRequests { get; set; }
	}
}

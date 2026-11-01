namespace Resgrid.Model.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// The header every activation-scoped Incident Back Office widget carries (plan §7.10). HasActivation false is the empty state
	/// ("no open incident", or the chosen incident no longer exists).
	/// </summary>
	public abstract class IncidentWidgetDataBase
	{
		/// <summary>The add-on lapsed: the module reads but refuses writes (the lapsed banner).</summary>
		public bool ReadOnly { get; set; }

		public bool HasActivation { get; set; }

		public string ActivationId { get; set; }

		public string ActivationName { get; set; }

		public string ShortCode { get; set; }

		/// <summary>IncidentActivationStatuses name (Preparing, Active, Demobilizing, Closed, Sealed) — W.ibo.Status.{name}.</summary>
		public string ActivationStatus { get; set; }

		public bool IsExercise { get; set; }

		/// <summary>The activation's currency (finance figures are in it).</summary>
		public string Currency { get; set; }
	}

	/// <summary>A labelled count (support status, resource kind…). Key is an enum name the client localizes.</summary>
	public sealed class IncidentKeyCount
	{
		public string Key { get; set; }

		public int Count { get; set; }
	}

	/// <summary>Arrivals or departures in one time window (2h, 6h, 12h, 24h).</summary>
	public sealed class IncidentArrivalWindow
	{
		public string Window { get; set; }

		public System.DateTime? Until { get; set; }

		public int Count { get; set; }

		public int Apparatus { get; set; }

		public int Personnel { get; set; }

		/// <summary>Outbound only: resources whose assignment times out in the window without a demob plan.</summary>
		public int Predicted { get; set; }
	}
}

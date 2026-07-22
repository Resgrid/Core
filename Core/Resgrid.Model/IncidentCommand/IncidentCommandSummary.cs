using System;

namespace Resgrid.Model
{
	/// <summary>
	/// List-card projection of an incident command for the IC app's incident list: identity, lifecycle timing,
	/// resolved commander name, locations, and active resource counts. Not persisted (composed by the service).
	/// </summary>
	public class IncidentCommandSummary
	{
		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Commander-supplied incident name; null when unnamed (UIs fall back to the call name).</summary>
		public string Name { get; set; }

		public string CallName { get; set; }

		public string CallNumber { get; set; }

		public string CallAddress { get; set; }

		/// <summary>Maps to <see cref="IncidentCommandStatus"/>.</summary>
		public int Status { get; set; }

		public DateTime EstablishedOn { get; set; }

		public DateTime? ClosedOn { get; set; }

		public string CommanderUserId { get; set; }

		/// <summary>Resolved commander full name (falls back to the user id when the profile is unavailable).</summary>
		public string CommanderName { get; set; }

		public string CommandPostLocationText { get; set; }

		public string CommandPostLatitude { get; set; }

		public string CommandPostLongitude { get; set; }

		/// <summary>Active (unreleased) personnel assignments placed in a lane or staging.</summary>
		public int AssignedPersonnelCount { get; set; }

		/// <summary>Active (unreleased) unit assignments placed in a lane or staging.</summary>
		public int AssignedUnitCount { get; set; }
	}

	/// <summary>
	/// Field bag for <c>IIncidentCommandService.UpdateCommandInfoAsync</c>. Null members are left unchanged;
	/// an empty string clears the stored value. Any location whose text is set while its coordinates are
	/// blank is geocoded server-side on save.
	/// </summary>
	public class IncidentCommandInfoUpdate
	{
		public string Name { get; set; }

		/// <summary>Corrected incident start time (UTC); null leaves the original EstablishedOn.</summary>
		public DateTime? EstablishedOn { get; set; }

		public DateTime? EstimatedEndOn { get; set; }

		/// <summary>When true, a null <see cref="EstimatedEndOn"/> clears the stored value instead of leaving it.</summary>
		public bool ClearEstimatedEndOn { get; set; }

		public string ImportantInformation { get; set; }

		public int? IcsLevel { get; set; }

		public string CommandPostLocationText { get; set; }

		public string CommandPostLatitude { get; set; }

		public string CommandPostLongitude { get; set; }

		public string StagingLocationText { get; set; }

		public string StagingLatitude { get; set; }

		public string StagingLongitude { get; set; }

		public string RehabLocationText { get; set; }

		public string RehabLatitude { get; set; }

		public string RehabLongitude { get; set; }
	}
}

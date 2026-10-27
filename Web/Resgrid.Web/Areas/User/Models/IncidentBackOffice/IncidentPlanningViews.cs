using System;
using System.Collections.Generic;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Web.Areas.User.Models.IncidentBackOffice
{
	/// <summary>Operational periods and plan versions of one activation (plan §9.1).</summary>
	public sealed class IncidentPeriodsView : IncidentDeskView
	{
		public List<IncidentOperationalPeriod> Periods { get; set; } = new List<IncidentOperationalPeriod>();
		public List<IncidentPlanVersion> Plans { get; set; } = new List<IncidentPlanVersion>();
		public bool CanManage { get; set; }
		public bool CanPlan { get; set; }
		/// <summary>Suggested window of the next period (incident-zone input values).</summary>
		public string NextStart { get; set; }
		public string NextEnd { get; set; }
		public Dictionary<string, string> People { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>The position assignment form (plan §9.2): qualification shown before the save, conflicts never silently double-booked.</summary>
	public sealed class IncidentAssignView : IncidentDeskView
	{
		public IncidentPositionAssignment Assignment { get; set; } = new IncidentPositionAssignment();
		public string StartOn { get; set; }
		public List<LedgerOption> Positions { get; set; } = new List<LedgerOption>();
		public List<LedgerOption> People { get; set; } = new List<LedgerOption>();
		public List<LedgerOption> Periods { get; set; } = new List<LedgerOption>();
		public List<LedgerOption> Nodes { get; set; } = new List<LedgerOption>();
		public IncidentAssignmentResult Result { get; set; }
		public bool CanOverride { get; set; }
		public Dictionary<string, string> Errors { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	/// <summary>The enrollment queue (plan §9.5): operational subjects without a support record.</summary>
	public sealed class IncidentEnrollmentView : IncidentDeskView
	{
		public List<IncidentEnrollmentCandidate> Candidates { get; set; } = new List<IncidentEnrollmentCandidate>();
		public bool CanEnroll { get; set; }
	}

	/// <summary>Roster import (plan §9.5): stage, review the errors, commit the valid rows.</summary>
	public sealed class IncidentRosterImportView : IncidentDeskView
	{
		public string Csv { get; set; }
		public IncidentImportStage Stage { get; set; }
		public int? Committed { get; set; }
	}

	/// <summary>Desk check-in by code or by name (plan §9.5).</summary>
	public sealed class IncidentCheckInDeskView : IncidentDeskView
	{
		public string Code { get; set; }
		public IncidentParticipant Participant { get; set; }
		public bool NotFound { get; set; }
		public List<IncidentParticipant> Expected { get; set; } = new List<IncidentParticipant>();
		public bool CanCheckIn { get; set; }
	}

	/// <summary>Confirms seeding one standard position catalog (adds missing positions only).</summary>
	public sealed class IncidentSeedView : IncidentDeskView
	{
		public IncidentPositionSources Catalog { get; set; }
	}

	/// <summary>A standing team's readiness for a window and its activation proposal (plan §9.2).</summary>
	public sealed class IncidentTeamReadinessView : IncidentDeskView
	{
		public List<DepartmentIncidentTeam> Teams { get; set; } = new List<DepartmentIncidentTeam>();
		public string TeamId { get; set; }
		public string WindowStart { get; set; }
		public string WindowEnd { get; set; }
		public IncidentTeamReadinessResult Readiness { get; set; }
		public List<IncidentTeamActivationProposal> Proposals { get; set; } = new List<IncidentTeamActivationProposal>();
		public List<LedgerOption> Activations { get; set; } = new List<LedgerOption>();
		public string ActivationId { get; set; }
		public bool CanActivate { get; set; }
		public IncidentTeamActivationResult Result { get; set; }
	}
}

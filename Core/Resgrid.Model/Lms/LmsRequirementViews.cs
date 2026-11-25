using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>A requirement as the editor saves it (new when <see cref="LmsRequirementId"/> is 0).</summary>
	public sealed class LmsRequirementInput
	{
		public int LmsRequirementId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public LmsRequirementKinds Kind { get; set; }
		public int? TargetRefId { get; set; }
		public string TargetCode { get; set; }
		public decimal? RequiredHours { get; set; }
		public int? RequiredCount { get; set; }
		public LmsRequirementRecurrences Recurrence { get; set; }
		public int? IntervalMonths { get; set; }
		public int DueDays { get; set; } = LmsRequirement.DefaultDueDays;
		public int GraceDays { get; set; }
		public int? AtRiskDays { get; set; }
		public bool NewMembersOnly { get; set; }
		public bool AutoAssign { get; set; }
		public bool IsOnboarding { get; set; }
		public int? BacksCertificationTypeId { get; set; }
		public bool EnforcementEnabled { get; set; }
		public bool AlertSupervisors { get; set; }
		public List<string> AlertUserIds { get; set; } = new List<string>();
		public bool AlertOnCompletion { get; set; }
		public bool IsActive { get; set; } = true;

		/// <summary>For a requirement on an Academy lesson: <see cref="LmsMajorChangePolicies"/> on a major Academy update (default Notify).</summary>
		public LmsMajorChangePolicies AcademyUpdatePolicy { get; set; } = LmsMajorChangePolicies.Notify;
		public List<ContentAudienceRule> Audience { get; set; } = new List<ContentAudienceRule>();
	}

	/// <summary>A requirement on the Programs list: its audience, what it targets and how its members stand.</summary>
	public sealed class LmsRequirementRow
	{
		public LmsRequirement Requirement { get; set; }
		public List<LmsRequirementAudience> Audience { get; set; } = new List<LmsRequirementAudience>();
		public string TargetName { get; set; }
		public Dictionary<LmsRequirementStates, int> Counts { get; set; } = new Dictionary<LmsRequirementStates, int>();
		public int Members => Counts.Values.Sum();
	}

	/// <summary>One cell of the compliance matrix (person × requirement).</summary>
	public sealed class LmsMatrixCell
	{
		public int LmsRequirementId { get; set; }
		public int LmsRequirementMemberId { get; set; }
		public LmsRequirementStates State { get; set; }
		public DateTime? DueUtc { get; set; }
		public DateTime? LastCompletedUtc { get; set; }
		public decimal Progress { get; set; }
		public decimal Required { get; set; }
		public bool Counted { get; set; }
		public string ExemptReason { get; set; }
		public DateTime? ExtendedDueUtc { get; set; }
		public bool Enforced { get; set; }
	}

	public sealed class LmsMatrixRow
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public Dictionary<int, LmsMatrixCell> Cells { get; set; } = new Dictionary<int, LmsMatrixCell>();
	}

	public sealed class LmsMatrixFilter
	{
		public int? LmsRequirementId { get; set; }
		public int? DepartmentGroupId { get; set; }
		public LmsRequirementStates? State { get; set; }
	}

	/// <summary>
	/// The compliance matrix (T3 item 2): active members in the viewer's scope × live requirements. Not licensed members show as such
	/// (never overdue); inactive members are never listed. While the add-on is lapsed the matrix is read-only and paused (rule 5).
	/// </summary>
	public sealed class LmsComplianceMatrix
	{
		public List<LmsRequirement> Requirements { get; set; } = new List<LmsRequirement>();

		/// <summary>Every live requirement (the filter's choices), whatever the filter.</summary>
		public List<LmsRequirement> AllRequirements { get; set; } = new List<LmsRequirement>();
		public Dictionary<int, string> TargetNames { get; set; } = new Dictionary<int, string>();
		public List<LmsMatrixRow> Rows { get; set; } = new List<LmsMatrixRow>();
		public Dictionary<LmsRequirementStates, int> Totals { get; set; } = new Dictionary<LmsRequirementStates, int>();
		public bool Paused { get; set; }
		public bool CanEdit { get; set; }
		public DateTime AsOfUtc { get; set; }
		public LmsErrors Error { get; set; }
	}

	/// <summary>A requirement on the member's own My Training page (and the v4 API).</summary>
	public sealed class LmsMyRequirement
	{
		public LmsRequirement Requirement { get; set; }
		public LmsRequirementStatus Status { get; set; }
		public string TargetName { get; set; }

		/// <summary>The LMS course to open (course requirements), when there is one.</summary>
		public int? CourseId { get; set; }

		/// <summary>The learning path to open (path requirements), when there is one.</summary>
		public int? PathId { get; set; }

		/// <summary>The baseline training to open (training requirements), when there is one.</summary>
		public int? TrainingId { get; set; }
	}

	/// <summary>One action an enforcement run would take: the member's certification expires and these roles would be lost.</summary>
	public sealed class LmsEnforcementPreviewRow
	{
		public LmsRequirement Requirement { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public int PersonnelCertificationId { get; set; }
		public string CertificationTypeName { get; set; }
		public DateTime? DueUtc { get; set; }
		public List<LmsRoleImpact> RolesLost { get; set; } = new List<LmsRoleImpact>();
	}

	/// <summary>
	/// "Who would lose which role today" (R-L11): what the next enforcement run would do, and whether the blast-radius guard would refuse it.
	/// Roles are lost only when the certification module enforces role requirements (Enforce), after its own grace period.
	/// </summary>
	public sealed class LmsEnforcementPreview
	{
		public bool DepartmentEnabled { get; set; }
		public int MaxSharePercent { get; set; }
		public int CertificationEnforcementMode { get; set; }
		public int RoleRemovalGraceDays { get; set; }
		public List<LmsEnforcementPreviewRow> Rows { get; set; } = new List<LmsEnforcementPreviewRow>();
		public List<LmsRoleBreach> Breaches { get; set; } = new List<LmsRoleBreach>();
		public bool WouldRefuse => Breaches.Count > 0;
		public bool Paused { get; set; }
		public LmsErrors Error { get; set; }
	}

	/// <summary>What one engine run did (worker 93, worker 83's hook).</summary>
	public sealed class LmsRequirementRunResult
	{
		public bool Skipped { get; set; }
		public bool Paused { get; set; }
		public int MembersAdded { get; set; }
		public int MembersLeft { get; set; }
		public int Transitions { get; set; }
		public int Notified { get; set; }
		public int Assigned { get; set; }
		public int Enforced { get; set; }
		public int Undone { get; set; }
		public bool Refused { get; set; }
		public int PathsAdvanced { get; set; }
	}

	/// <summary>A path step as the editor posts it.</summary>
	public sealed class LmsPathStepInput
	{
		public LmsPathStepKinds Kind { get; set; }
		public int TargetRefId { get; set; }
		public string Prerequisites { get; set; }
		public int? ElectiveGroup { get; set; }
		public int ElectiveRequired { get; set; }
	}

	public sealed class LmsPathInput
	{
		public int LmsPathId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int? IssuesCertificationTypeId { get; set; }
		public List<LmsPathStepInput> Steps { get; set; } = new List<LmsPathStepInput>();
	}

	/// <summary>A path step with its target's title.</summary>
	public sealed class LmsPathStepView
	{
		public LmsPathStep Step { get; set; }
		public string Title { get; set; }
	}

	public sealed class LmsPathRow
	{
		public LmsPath Path { get; set; }
		public string Title { get; set; }
		public int Steps { get; set; }
		public int Assigned { get; set; }
		public int Completed { get; set; }
	}

	public sealed class LmsPathDetail
	{
		public LmsPath Path { get; set; }
		public string Title { get; set; }
		public List<LmsPathStepView> Steps { get; set; } = new List<LmsPathStepView>();
		public bool CanEdit { get; set; }
		public bool CanAssign { get; set; }
	}

	/// <summary>One of the member's paths with their progress (My Training, the v4 API).</summary>
	public sealed class LmsMyPath
	{
		public LmsPathAssignment Assignment { get; set; }
		public LmsPath Path { get; set; }
		public string Title { get; set; }
		public LmsPathProgressResult Progress { get; set; }
		public Dictionary<int, string> StepTitles { get; set; } = new Dictionary<int, string>();

		/// <summary>The open enrollment (or training) each available course step runs in, by step position.</summary>
		public Dictionary<int, int> StepEnrollmentIds { get; set; } = new Dictionary<int, int>();
	}

	public sealed class LmsPathProgressRow
	{
		public string UserId { get; set; }
		public DateTime AssignedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public int Done { get; set; }
		public int Needed { get; set; }
	}
}

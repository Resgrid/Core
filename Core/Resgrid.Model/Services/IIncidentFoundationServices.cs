using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Model.Services
{
	// Incident Back Office LI-A Foundation services (large-incident-logistics-finance-plan.md §13.1). Every service here takes
	// read-only dependencies on IC/Dispatch services and calls only their Get* members (§3.2); writes go to this module's tables.

	/// <summary>§9.0 activations, complexes, planned events, EOC activations and exercises.</summary>
	public interface IIncidentActivationService
	{
		Task<IReadOnlyList<IncidentActivation>> GetActivationsAsync(IncidentActor actor, bool includeClosed = false, CancellationToken cancellationToken = default);
		Task<IncidentActivationContext> GetContextAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default);
		Task<IncidentActivation> OpenAsync(IncidentActor actor, IncidentActivationInput input, CancellationToken cancellationToken = default);
		Task<IncidentActivation> UpdateAsync(IncidentActor actor, IncidentActivation activation, int expectedVersion, CancellationToken cancellationToken = default);
		Task<IncidentActivation> LinkAsync(IncidentActor actor, string activationId, int? callId, string incidentCommandId, CancellationToken cancellationToken = default);
		Task<IncidentActivation> SetComplexAsync(IncidentActor actor, string activationId, string parentActivationId, CancellationToken cancellationToken = default);
		/// <summary>Demobilizing, Closed (closeout gate or waivers), Sealed, or a reasoned reopen.</summary>
		Task<IncidentActivation> ChangeStatusAsync(IncidentActor actor, string activationId, IncidentActivationStatuses status, string reason, CancellationToken cancellationToken = default);
		/// <summary>Members of a complex (or the activation itself) — the roll-up scope of a desk.</summary>
		Task<IReadOnlyList<string>> GetScopeAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default);
	}

	/// <summary>§7 authority profiles, snapshots and the reconciliation queue.</summary>
	public interface IIncidentAuthorityService
	{
		Task<DepartmentIncidentAuthorityProfile> GetDefaultProfileAsync(IncidentActor actor, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<IncidentDomainAuthority>> GetDomainsAsync(IncidentActor actor, IncidentAuthorityOwnerKinds ownerKind, string ownerId, CancellationToken cancellationToken = default);
		Task SaveDomainsAsync(IncidentActor actor, string profileId, IReadOnlyList<IncidentDomainAuthority> domains, CancellationToken cancellationToken = default);
		Task<IncidentAuthorityProfileSnapshot> SnapshotAsync(IncidentActor actor, string activationId, string profileId, CancellationToken cancellationToken = default);
	}

	/// <summary>§9.1 operational periods, plan assembly, weather blocks and objectives.</summary>
	public interface IIncidentPlanningService
	{
		Task<IReadOnlyList<IncidentOperationalPeriod>> GetPeriodsAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default);
		Task<IncidentOperationalPeriod> GetCurrentPeriodAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default);
		Task<IncidentOperationalPeriod> CreatePeriodAsync(IncidentActor actor, string activationId, DateTime startUtc, DateTime endUtc, DateTime? briefingUtc, CancellationToken cancellationToken = default);
		/// <summary>Makes a period current (the previous current one closes) and emits trigger 116.</summary>
		Task<IncidentOperationalPeriod> StartPeriodAsync(IncidentActor actor, string periodId, CancellationToken cancellationToken = default);
		/// <summary>The next period with open objectives, organization nodes and running assignments copied forward; completed transactions never copy.</summary>
		Task<IncidentOperationalPeriod> CopyForwardAsync(IncidentActor actor, string fromPeriodId, CancellationToken cancellationToken = default);
		Task<IncidentOperationalPeriod> FreezeAsync(IncidentActor actor, string periodId, bool approve, CancellationToken cancellationToken = default);
		Task<IncidentPlanVersion> AssemblePlanAsync(IncidentActor actor, string periodId, IncidentPlanManifest manifest, CancellationToken cancellationToken = default);
		/// <summary>Approves an assembled plan version (emits 117) and snapshots its weather block.</summary>
		Task<IncidentPlanVersion> ApprovePlanAsync(IncidentActor actor, string planVersionId, CancellationToken cancellationToken = default);
	}

	/// <summary>§9.2 position catalog, assignments, organization nodes and standing team rosters.</summary>
	public interface IIncidentOrganizationService
	{
		Task<IReadOnlyList<IncidentPositionDefinition>> GetPositionsAsync(IncidentActor actor, string activationId = null, CancellationToken cancellationToken = default);
		/// <summary>Adds the missing positions of a standard catalog (NIMS core, NWCG, CICCS); never changes an existing row.</summary>
		Task<int> SeedCatalogAsync(IncidentActor actor, IncidentPositionSources catalog, CancellationToken cancellationToken = default);
		Task<IncidentAssignmentResult> AssignAsync(IncidentActor actor, IncidentPositionAssignment assignment, bool overrideQualification, CancellationToken cancellationToken = default);
		Task<IncidentPositionAssignment> EndAssignmentAsync(IncidentActor actor, string assignmentId, DateTime? endUtc, CancellationToken cancellationToken = default);
		/// <summary>Who held each position at <paramref name="atUtc"/> (the effective-dated question of §9.2).</summary>
		Task<IReadOnlyList<IncidentPositionAssignment>> GetHoldersAsync(IncidentActor actor, string activationId, DateTime atUtc, CancellationToken cancellationToken = default);
		Task<IncidentQualificationCheck> CheckQualificationAsync(IncidentActor actor, IncidentPositionDefinition position, string userId, CancellationToken cancellationToken = default);
		/// <summary>Computed roster readiness for a window (never stored).</summary>
		Task<IncidentTeamReadinessResult> GetTeamReadinessAsync(IncidentActor actor, string teamId, DateTime windowStartUtc, DateTime windowEndUtc, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<IncidentTeamActivationProposal>> ProposeTeamActivationAsync(IncidentActor actor, string teamId, string activationId, CancellationToken cancellationToken = default);
		/// <summary>One transaction: the chosen candidate per slot becomes a position assignment; conflicts are reported, not double-booked.</summary>
		Task<IncidentTeamActivationResult> ActivateTeamAsync(IncidentActor actor, string teamId, string activationId, string periodId, IReadOnlyList<IncidentTeamActivationChoice> choices, CancellationToken cancellationToken = default);
	}

	/// <summary>§9.2 participants, §9.3 qualification snapshots, §9.5 enrollment queue and desk check-in.</summary>
	public interface IIncidentParticipantsService
	{
		Task<IReadOnlyList<IncidentEnrollmentCandidate>> GetEnrollmentQueueAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default);
		Task<IncidentEnrollmentResult> EnrollAsync(IncidentActor actor, string activationId, IReadOnlyList<string> candidateKeys, bool asDraft, CancellationToken cancellationToken = default);
		Task<int> SnapshotQualificationsAsync(IncidentActor actor, string participantId, CancellationToken cancellationToken = default);
		Task<IncidentParticipant> CheckInAsync(IncidentActor actor, string participantId, IncidentCheckInSources source, CancellationToken cancellationToken = default);
		Task<IncidentParticipant> FindByCheckInCodeAsync(IncidentActor actor, string code, CancellationToken cancellationToken = default);
		/// <summary>Stages a roster CSV (header row; DisplayName required) and returns rows with their validation errors; commits nothing.</summary>
		Task<IncidentImportStage> StageRosterImportAsync(IncidentActor actor, string activationId, string csv, CancellationToken cancellationToken = default);
		/// <summary>Commits the valid staged rows; repeatable — a row whose (agency, order, name) already exists is skipped.</summary>
		Task<int> CommitRosterImportAsync(IncidentActor actor, string activationId, string csv, CancellationToken cancellationToken = default);
	}

	/// <summary>§13.8 native notices addressed to whoever currently holds an incident position.</summary>
	public interface IIncidentNotificationRoutingService
	{
		Task<IReadOnlyList<string>> ResolveHoldersAsync(IncidentActor actor, IncidentActivation activation, string positionCode, CancellationToken cancellationToken = default);
		/// <summary>Resolves the notice's configured positions (setting 87), delivers in each recipient's language and logs the resolution.</summary>
		Task<int> NotifyPositionsAsync(IncidentActor actor, IncidentActivation activation, Events.EventTypes notice, Func<System.Globalization.CultureInfo, string> message, IReadOnlyList<string> positionOverride = null, CancellationToken cancellationToken = default);
		/// <summary>One person's own notice (assignment, lodging, release) — never anyone else's data.</summary>
		Task<bool> NotifyUserAsync(IncidentActor actor, IncidentActivation activation, Events.EventTypes notice, string userId, Func<System.Globalization.CultureInfo, string> message, CancellationToken cancellationToken = default);
	}

	/// <summary>§9.17 desks and §13.4 snapshot semantics.</summary>
	public interface IIncidentOperationalIntelligenceService
	{
		Task<IncidentDeskSummary> GetDeskSummaryAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<IncidentPersonnelPictureRow>> GetPersonnelPictureAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default);
		/// <summary>Worker 74: one incremental snapshot per open activation.</summary>
		Task<IncidentOperationalSnapshot> ProjectSnapshotAsync(IncidentActor actor, string activationId, IncidentSnapshotKinds kind, CancellationToken cancellationToken = default);
	}

	/// <summary>§12.3 the read seam IC uses for derived need numbers; null when the module is off for the department.</summary>
	public interface IIncidentRequestLinkReader
	{
		Task<IReadOnlyDictionary<string, IncidentNeedDerivation>> GetNeedDerivationsAsync(int departmentId, IReadOnlyList<string> incidentNeedIds, CancellationToken cancellationToken = default);
	}

	/// <summary>§10A.3 projection contracts served to RMS and authorized integrations: references and safe display fields, never protected values.</summary>
	public interface IIncidentProjectionService
	{
		Task<IncidentProjection<IncidentPersonnelCheckInRowV1>> GetPersonnelCheckInV1Async(int departmentId, string activationId, CancellationToken cancellationToken = default);
		Task<IncidentProjection<IncidentResourceSummaryRowV1>> GetResourceSummaryV1Async(int departmentId, string activationId, CancellationToken cancellationToken = default);
	}
}

namespace Resgrid.Model.IncidentLogistics
{
	public sealed class IncidentActivationInput
	{
		public IncidentActivationKinds Kind { get; set; }
		public string Name { get; set; }
		public string ShortCode { get; set; }
		public int? ComplexityType { get; set; }
		public int? CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string ParentActivationId { get; set; }
		public string TimeZoneId { get; set; }
		public string Currency { get; set; }
		public string MeasurementSystem { get; set; }
		public bool IsExercise { get; set; }
		public string AuthorityProfileId { get; set; }
		public string Description { get; set; }
		public bool StartActive { get; set; } = true;
		/// <summary>Create the first operational period now (length from setting 85).</summary>
		public bool CreateFirstPeriod { get; set; } = true;
	}

	/// <summary>What every incident-scoped desk shows in its context bar (plan §14.5).</summary>
	public sealed class IncidentActivationContext
	{
		public IncidentActivation Activation { get; set; }
		public IncidentActivation Parent { get; set; }
		public IReadOnlyList<IncidentActivation> Members { get; set; } = Array.Empty<IncidentActivation>();
		public IncidentOperationalPeriod CurrentPeriod { get; set; }
		public IncidentAuthorityProfileSnapshot Authority { get; set; }
		public DateTime? SnapshotOn { get; set; }
		public bool IsStale { get; set; }
	}

	public sealed class IncidentPlanManifest
	{
		public List<IncidentPlanManifestItem> Items { get; set; } = new List<IncidentPlanManifestItem>();
		public List<string> Maps { get; set; } = new List<string>();
		public List<string> Distribution { get; set; } = new List<string>();
		public int PrintCopies { get; set; }
	}

	public sealed class IncidentPlanManifestItem
	{
		public string RecordId { get; set; }
		public string RevisionId { get; set; }
		public string DefinitionKey { get; set; }
		public string Title { get; set; }
		public int Order { get; set; }
	}

	public sealed class IncidentQualificationCheck
	{
		public IncidentQualificationStates State { get; set; }
		public bool Blocked { get; set; }
		public List<string> Missing { get; set; } = new List<string>();
		public List<string> Expiring { get; set; } = new List<string>();
		public int EnforcementMode { get; set; }
	}

	public sealed class IncidentAssignmentResult
	{
		public IncidentPositionAssignment Assignment { get; set; }
		public IncidentQualificationCheck Qualification { get; set; }
		public List<IncidentPositionAssignment> Conflicts { get; set; } = new List<IncidentPositionAssignment>();
	}

	public sealed class IncidentSlotReadinessRow
	{
		public DepartmentIncidentTeamSlot Slot { get; set; }
		public IncidentPositionDefinition Position { get; set; }
		public IncidentSlotReadiness Readiness { get; set; }
		public string CandidateId { get; set; }
		public string CandidateName { get; set; }
		public List<string> Missing { get; set; } = new List<string>();
	}

	public sealed class IncidentTeamReadinessResult
	{
		public DepartmentIncidentTeam Team { get; set; }
		public IncidentTeamReadiness Readiness { get; set; }
		public DateTime WindowStart { get; set; }
		public DateTime WindowEnd { get; set; }
		public List<IncidentSlotReadinessRow> Slots { get; set; } = new List<IncidentSlotReadinessRow>();
		public int Gaps => Slots.FindAll(s => s.Readiness is IncidentSlotReadiness.MissingRequirement or IncidentSlotReadiness.NoCandidate).Count;
	}

	public sealed class IncidentTeamActivationProposal
	{
		public DepartmentIncidentTeamSlot Slot { get; set; }
		public IncidentPositionDefinition Position { get; set; }
		public DepartmentIncidentTeamCandidate Candidate { get; set; }
		public string CandidateName { get; set; }
		public IncidentSlotReadiness Readiness { get; set; }
		/// <summary>The candidate already holds an open assignment elsewhere (a conflict, never silently double-booked).</summary>
		public string ConflictActivationName { get; set; }
	}

	public sealed class IncidentTeamActivationChoice
	{
		public string SlotId { get; set; }
		/// <summary>The chosen candidate; null leaves the slot unfilled (a gap).</summary>
		public string CandidateId { get; set; }
	}

	public sealed class IncidentTeamActivationResult
	{
		public int Assigned { get; set; }
		public int Gaps { get; set; }
		public List<string> Conflicts { get; set; } = new List<string>();
	}

	/// <summary>An operational subject without a support record (plan §9.5) — a member or unit dispatched or on the board, or an IC ad-hoc entry.</summary>
	public sealed class IncidentEnrollmentCandidate
	{
		/// <summary>"user:{id}", "unit:{id}", "adhoc-person:{id}" or "adhoc-unit:{id}".</summary>
		public string Key { get; set; }
		public string Name { get; set; }
		public string Kind { get; set; }
		public string Source { get; set; }
		public DateTime? SeenSince { get; set; }
	}

	public sealed class IncidentEnrollmentResult
	{
		public int Participants { get; set; }
		public int Resources { get; set; }
		public int Skipped { get; set; }
	}

	public sealed class IncidentImportStage
	{
		public List<IncidentImportRow> Rows { get; set; } = new List<IncidentImportRow>();
		public int Valid => Rows.FindAll(r => r.Errors.Count == 0).Count;
	}

	public sealed class IncidentImportRow
	{
		public int Line { get; set; }
		public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<string> Errors { get; set; } = new List<string>();
		public bool IsDuplicate { get; set; }
	}

	/// <summary>The numbers a desk opens on (plan §9.17), each with its own as-of time.</summary>
	public sealed class IncidentDeskSummary
	{
		public DateTime AsOf { get; set; }
		public Dictionary<string, decimal> Values { get; set; } = new Dictionary<string, decimal>(StringComparer.Ordinal);
		public List<IncidentArrivalBucket> Inbound { get; set; } = new List<IncidentArrivalBucket>();
		public List<IncidentArrivalBucket> Outbound { get; set; } = new List<IncidentArrivalBucket>();

		public decimal this[string key] => Values.TryGetValue(key, out var value) ? value : 0m;
	}

	/// <summary>Inbound (fills by ETA) or outbound (committed and predicted departures) apparatus and personnel by window (plan §9.17.1–2).</summary>
	public sealed class IncidentArrivalBucket
	{
		public string Window { get; set; }
		public DateTime? Until { get; set; }
		public int Apparatus { get; set; }
		public int Personnel { get; set; }
		public int Predicted { get; set; }
		public int Count { get; set; }
	}

	/// <summary>Two status columns, never one (plan §9.5): support status owned here, operational status sourced from Dispatch with its time.</summary>
	public sealed class IncidentPersonnelPictureRow
	{
		public string ParticipantId { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public string Agency { get; set; }
		public IncidentSupportStatuses SupportStatus { get; set; }
		public string OperationalStatus { get; set; }
		public DateTime? OperationalStatusOn { get; set; }
		public bool IsUnmatched { get; set; }
		public string Position { get; set; }
	}

	public sealed class IncidentNeedDerivation
	{
		public int LinkedRequestCount { get; set; }
		public decimal DerivedQuantityFulfilled { get; set; }
		public string DerivedStatus { get; set; }
	}

	/// <summary>A §10A.3 contract response: unavailable (module disabled) is a state, never an empty-but-successful payload.</summary>
	public sealed class IncidentProjection<TRow>
	{
		public string Contract { get; set; }
		public int Version { get; set; } = 1;
		public bool Available { get; set; }
		public long Cursor { get; set; }
		public DateTime? OldestSourceOn { get; set; }
		public IReadOnlyList<string> ExcludedFields { get; set; } = Array.Empty<string>();
		public List<TRow> Rows { get; set; } = new List<TRow>();
	}

	public sealed class IncidentPersonnelCheckInRowV1
	{
		public string ParticipantId { get; set; }
		public string Name { get; set; }
		public string Agency { get; set; }
		public string HomeUnit { get; set; }
		public string Position { get; set; }
		public string OrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public DateTime? CheckedInOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public string SupportStatus { get; set; }
	}

	public sealed class IncidentResourceSummaryRowV1
	{
		public string ResourceId { get; set; }
		public string Name { get; set; }
		public string Kind { get; set; }
		public string ResourceType { get; set; }
		public string Provider { get; set; }
		public string OrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public int PersonnelCount { get; set; }
		public DateTime? ArrivedOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public string SupportStatus { get; set; }
		public int ComponentCount { get; set; }
	}
}

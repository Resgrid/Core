using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Everything the NERIS mapping needs from one incident report, read once and handed to a pure mapper
	/// (RMS plan section 5.5). Built by the incident report service from the aggregate; the mapper never
	/// touches a repository.
	/// </summary>
	public class NerisIncidentSnapshot
	{
		public RecordUdfSection CustomFields { get; set; }
		/// <summary>Departmental revision content. The national mapper never emits these collections.</summary>
		public List<RmsRecordAttachment> Attachments { get; set; } = new List<RmsRecordAttachment>();
		public List<RmsEvidenceArtifact> Evidence { get; set; } = new List<RmsEvidenceArtifact>();
		public RmsIncidentReport Report { get; set; }
		public RmsLocation Location { get; set; }
		public List<RmsIncidentType> Types { get; set; } = new List<RmsIncidentType>();
		public List<RmsUnitResponse> Units { get; set; } = new List<RmsUnitResponse>();
		public List<RmsAid> Aids { get; set; } = new List<RmsAid>();
		public List<RmsActionTactic> Tactics { get; set; } = new List<RmsActionTactic>();
		public RmsNarrative Narrative { get; set; }
		public List<RmsSourceFact> Facts { get; set; } = new List<RmsSourceFact>();
		/// <summary>Dispatch comments (call notes) carried into dispatch.comments; header text only, never restricted content.</summary>
		public List<NerisDispatchComment> DispatchComments { get; set; } = new List<NerisDispatchComment>();
		/// <summary>NERIS special modifiers (MCI, declared disaster ...), value-set codes.</summary>
		public List<string> SpecialModifiers { get; set; } = new List<string>();

		/// <summary>RMS-3 conditional sections that ride the incident payload.</summary>
		public List<RmsIncidentModule> Modules { get; set; } = new List<RmsIncidentModule>();

		/// <summary>RMS-3 non-unit resources used on the incident.</summary>
		public List<RmsIncidentResource> Resources { get; set; } = new List<RmsIncidentResource>();

		/// <summary>RMS-3 casualties and rescues. Restricted, but they are reported facts and do enter the payload.</summary>
		public List<RmsCasualtyRescue> Casualties { get; set; } = new List<RmsCasualtyRescue>();

		/// <summary>RMS-3 exposures — property other than the incident property that the incident damaged.</summary>
		public List<RmsExposure> Exposures { get; set; } = new List<RmsExposure>();
	}

	/// <summary>
	/// Everything the incident-analysis mapping needs, read once and handed to the pure mapper (RMS-3). Carries the
	/// incident's NERIS id because the analysis is posted against the incident, not against the department.
	/// </summary>
	public class NerisIncidentAnalysisSnapshot
	{
		public RmsIncidentAnalysis Analysis { get; set; }
		public RmsIncidentReport Report { get; set; }
		public List<RmsIncidentModule> Modules { get; set; } = new List<RmsIncidentModule>();
		public List<RmsIncidentProperty> Properties { get; set; } = new List<RmsIncidentProperty>();
		public List<RmsIncidentVehicle> Vehicles { get; set; } = new List<RmsIncidentVehicle>();
	}

	/// <summary>
	/// One conditional section the selected incident types demand or suggest (RMS plan section 4.2, RMS-3). The
	/// provider owns the rule; this is the shape the service and the clients read it in, so a client renders the
	/// same progressive requirements the server validates against instead of keeping its own copy of the rules.
	/// </summary>
	public class NerisSectionRequirement
	{
		public RmsIncidentModuleKind Kind { get; set; }

		/// <summary>True blocks finalization; false surfaces as a warning the author may answer or ignore.</summary>
		public bool Required { get; set; }

		/// <summary>Why the section applies, shown beside it in the authoring surface.</summary>
		public string Reason { get; set; }

		/// <summary>
		/// Value set the section's headline code must belong to, or null when the section has no coded headline.
		/// Carried here so an authoring surface builds its dropdown from the same rule validation enforces.
		/// </summary>
		public string PrimaryCodeSet { get; set; }

		/// <summary>Value set the section's second reportable code must belong to, or null when it has none.</summary>
		public string SecondaryCodeSet { get; set; }
	}

	public class NerisDispatchComment
	{
		public DateTime? Timestamp { get; set; }
		public string Comment { get; set; }
	}

	/// <summary>Department integration credential, decrypted only inside the provider for the duration of a call.</summary>
	public class NerisCredential
	{
		public string ClientId { get; set; }
		public string ClientSecret { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
	}

	public class NerisValueSet
	{
		public string SetKey { get; set; }
		public string SchemaName { get; set; }
		public IReadOnlyList<string> Codes { get; set; } = new List<string>();
	}

	public enum NerisOutcomeKind
	{
		/// <summary>The destination created the incident (first submission).</summary>
		Created = 1,
		/// <summary>The destination accepted an update to an existing incident.</summary>
		Updated = 2,
		/// <summary>The destination's status shows the incident approved/accepted.</summary>
		Accepted = 3,
		/// <summary>The destination rejected the payload (validation 422 or a REJECTED status).</summary>
		Rejected = 4,
		/// <summary>Network, throttling or 5xx: retry with the same idempotency key.</summary>
		Transient = 5,
		/// <summary>Configuration or authentication problem an operator must fix; no automatic retry.</summary>
		Fatal = 6,
		/// <summary>Created at the destination but still awaiting its review outcome.</summary>
		Pending = 7
	}

	/// <summary>One exchange with the destination, reduced to what the submission row and the workflow triggers may see.</summary>
	public class NerisSubmissionOutcome
	{
		/// <summary>The queued payload was refused locally before credentials or HTTP; no destination response is implied.</summary>
		public bool LocalValidationFailure { get; set; }
		public bool DeliveryUncertain { get; set; }
		public NerisOutcomeKind Kind { get; set; }
		public int? StatusCode { get; set; }
		public string ExternalId { get; set; }
		public string ExternalStatus { get; set; }
		/// <summary>Verbatim response body for the immutable artifact.</summary>
		public string ResponseJson { get; set; }
		/// <summary>Normalized, non-sensitive error codes and field paths.</summary>
		public List<NerisSubmissionError> Errors { get; set; } = new List<NerisSubmissionError>();
		public string Message { get; set; }
	}

	public class NerisSubmissionError
	{
		public string Code { get; set; }
		public string FieldPath { get; set; }
		public string Message { get; set; }
	}

	public class NerisIncidentStatus
	{
		public string NerisIncidentId { get; set; }
		public string Status { get; set; }
		public DateTime? LastModified { get; set; }
		public string CreatedBy { get; set; }
	}
}

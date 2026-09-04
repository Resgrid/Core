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

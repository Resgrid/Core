using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>Department NERIS profile, credential, value sets and crosswalks (RMS plan section 5.5).</summary>
	public interface INerisProfileService
	{
		/// <summary>Credential-free identity of the configured entity, environment and effective endpoint.</summary>
		string GetDestinationIdentity(RmsNerisProfile profile);
		Task<RmsNerisProfile> GetProfileAsync(int departmentId);

		/// <summary>Saves the profile; a non-null credential replaces the stored one (encrypted per department), null keeps it.</summary>
		Task<RmsNerisProfile> SaveProfileAsync(RmsNerisProfile profile, NerisCredential credential, string userId, CancellationToken cancellationToken = default);

		/// <summary>Decrypts the stored credential for one call; never cached, never logged.</summary>
		Task<NerisCredential> GetCredentialAsync(RmsNerisProfile profile);

		/// <summary>True when NerisConfig.Enabled, the profile is enabled, and it carries an entity ID and a credential.</summary>
		Task<bool> IsSubmissionEnabledAsync(int departmentId);

		/// <summary>A value set of the pinned contract, from the embedded snapshot (seeded to RmsNerisValueSets on first use).</summary>
		NerisValueSet GetValueSet(string setKey);

		IReadOnlyList<string> ValueSetKeys { get; }

		string ContractVersion { get; }

		Task EnsureValueSetsSeededAsync(CancellationToken cancellationToken = default);

		/// <summary>The department's mapped NERIS code for a local code, or null when unmapped.</summary>
		Task<string> ResolveCrosswalkAsync(int departmentId, string setKey, string localSource, string localCode);

		Task<List<RmsNerisCrosswalk>> GetCrosswalksAsync(int departmentId);

		Task<RmsNerisCrosswalk> SaveCrosswalkAsync(int departmentId, string userId, string setKey, string localSource, string localCode, string nerisCode, CancellationToken cancellationToken = default);

		/// <summary>Removes a department mapping so the local code is unmapped again; false when there was none.</summary>
		Task<bool> RemoveCrosswalkAsync(int departmentId, string setKey, string localSource, string localCode, CancellationToken cancellationToken = default);
	}

	/// <summary>Pure aggregate-to-payload mapping against the pinned contract; original codes ride beside mapped ones.</summary>
	public interface INerisMappingService
	{
		/// <summary>The exact IncidentPayload JSON the destination receives (the immutable submission artifact).</summary>
		string BuildIncidentPayloadJson(NerisIncidentSnapshot snapshot, RmsNerisProfile profile);

		/// <summary>The exact IncidentAnalysisPayload JSON for the separate fire/hazmat analysis filing (RMS-3).</summary>
		string BuildIncidentAnalysisPayloadJson(NerisIncidentAnalysisSnapshot snapshot, RmsNerisProfile profile);
	}

	/// <summary>Local (offline) validation against the pinned contract's requiredness, value sets and time sequence; remote validation through the client.</summary>
	public interface INerisValidationService
	{
		List<RmsValidationIssue> ValidateLocal(NerisIncidentSnapshot snapshot, RmsNerisProfile profile);

		/// <summary>Local validation of the separate incident-analysis filing (RMS-3); never blocks the incident.</summary>
		List<RmsValidationIssue> ValidateAnalysisLocal(NerisIncidentAnalysisSnapshot snapshot, RmsNerisProfile profile);

		/// <summary>
		/// The conditional sections a set of incident types demands or suggests (RMS-3 progressive section rules).
		/// Exposed so the authoring surfaces render exactly what validation will enforce.
		/// </summary>
		IReadOnlyList<NerisSectionRequirement> GetSectionRequirements(IEnumerable<string> incidentTypeCodes);

		Task<List<RmsValidationIssue>> ValidateRemoteAsync(RmsNerisProfile profile, string payloadJson, CancellationToken cancellationToken = default);
	}

	/// <summary>Low-level NERIS API boundary (token, validate, create, update, status, history).</summary>
	public interface INerisApiClient
	{
		Task<NerisSubmissionOutcome> ValidateAsync(RmsNerisProfile profile, NerisCredential credential, string payloadJson, CancellationToken cancellationToken = default);

		Task<NerisSubmissionOutcome> CreateIncidentAsync(RmsNerisProfile profile, NerisCredential credential, string payloadJson, CancellationToken cancellationToken = default);

		Task<NerisSubmissionOutcome> UpdateIncidentAsync(RmsNerisProfile profile, NerisCredential credential, string nerisIncidentId, string payloadJson, CancellationToken cancellationToken = default);

		Task<NerisSubmissionOutcome> GetStatusAsync(RmsNerisProfile profile, NerisCredential credential, string nerisIncidentId, CancellationToken cancellationToken = default);

		// Incident analysis (RMS-3): a second filing against an incident the destination already holds.
		Task<NerisSubmissionOutcome> CreateIncidentAnalysisAsync(RmsNerisProfile profile, NerisCredential credential, string nerisIncidentId, string payloadJson, CancellationToken cancellationToken = default);

		Task<NerisSubmissionOutcome> UpdateIncidentAnalysisAsync(RmsNerisProfile profile, NerisCredential credential, string nerisAnalysisId, string payloadJson, CancellationToken cancellationToken = default);

		Task<NerisSubmissionOutcome> GetIncidentAnalysisStatusAsync(RmsNerisProfile profile, NerisCredential credential, string nerisAnalysisId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Orchestrates one submission attempt: create on first delivery, update when the destination already holds the
	/// incident, status poll while the destination reviews. Never holds a database transaction (plan 5.3); the
	/// worker persists the outcome afterwards.
	/// </summary>
	public interface INerisSubmissionService
	{
		Task<NerisSubmissionOutcome> DeliverAsync(RmsNerisProfile profile, RmsSubmission submission, string existingNerisIncidentId, CancellationToken cancellationToken = default);

		Task<NerisSubmissionOutcome> CheckStatusAsync(RmsNerisProfile profile, string nerisIncidentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Delivers the incident-analysis filing (RMS-3). <paramref name="nerisIncidentId"/> is the incident the
		/// analysis files against and is required for the first delivery; <paramref name="existingNerisAnalysisId"/>
		/// switches the call to an update once the destination holds the analysis.
		/// </summary>
		Task<NerisSubmissionOutcome> DeliverAnalysisAsync(RmsNerisProfile profile, RmsSubmission submission, string nerisIncidentId, string existingNerisAnalysisId, CancellationToken cancellationToken = default);

		Task<NerisSubmissionOutcome> CheckAnalysisStatusAsync(RmsNerisProfile profile, string nerisAnalysisId, CancellationToken cancellationToken = default);
	}
}

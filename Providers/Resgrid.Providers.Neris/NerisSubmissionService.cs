using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// One delivery attempt (RMS plan sections 5.3/5.5): create on first delivery, update in place once the
	/// destination holds the incident, status poll while it reviews. Holds no database transaction; the worker
	/// persists the outcome and decides retry/backoff from the outcome kind.
	/// </summary>
	public class NerisSubmissionService : INerisSubmissionService
	{
		private readonly INerisApiClient _client;
		private readonly INerisProfileService _profiles;

		public NerisSubmissionService(INerisApiClient client, INerisProfileService profiles)
		{
			_client = client;
			_profiles = profiles;
		}

		public async Task<NerisSubmissionOutcome> DeliverAsync(RmsNerisProfile profile, RmsSubmission submission, string existingNerisIncidentId, CancellationToken cancellationToken = default)
		{
			if (submission == null) throw new ArgumentNullException(nameof(submission));
			var invalid = ValidateQueuedPayload(profile, submission, false);
			if (invalid != null) return invalid;

			var credential = await _profiles.GetCredentialAsync(profile);
			if (!string.IsNullOrWhiteSpace(existingNerisIncidentId))
			{
				var update = await _client.UpdateIncidentAsync(profile, credential, existingNerisIncidentId, submission.PayloadJson, cancellationToken);
				return update;
			}

			return await _client.CreateIncidentAsync(profile, credential, submission.PayloadJson, cancellationToken);
		}

		public async Task<NerisSubmissionOutcome> CheckStatusAsync(RmsNerisProfile profile, string nerisIncidentId, CancellationToken cancellationToken = default)
		{
			var credential = await _profiles.GetCredentialAsync(profile);
			return await _client.GetStatusAsync(profile, credential, nerisIncidentId, cancellationToken);
		}

		public async Task<NerisSubmissionOutcome> DeliverAnalysisAsync(RmsNerisProfile profile, RmsSubmission submission, string nerisIncidentId, string existingNerisAnalysisId, CancellationToken cancellationToken = default)
		{
			if (submission == null) throw new ArgumentNullException(nameof(submission));
			var invalid = ValidateQueuedPayload(profile, submission, true);
			if (invalid != null) return invalid;

			// The analysis files against an incident. Without the incident's id there is nothing to file against,
			// and that is a wait, not a failure: the incident's own submission is still in flight.
			if (string.IsNullOrWhiteSpace(nerisIncidentId) && string.IsNullOrWhiteSpace(existingNerisAnalysisId))
				return new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Transient, Message = "The incident is not filed yet; the analysis waits for it." };

			var credential = await _profiles.GetCredentialAsync(profile);
			if (!string.IsNullOrWhiteSpace(existingNerisAnalysisId))
			{
				var update = await _client.UpdateIncidentAnalysisAsync(profile, credential, existingNerisAnalysisId, submission.PayloadJson, cancellationToken);
				return update;
			}

			return await _client.CreateIncidentAnalysisAsync(profile, credential, nerisIncidentId, submission.PayloadJson, cancellationToken);
		}

		public async Task<NerisSubmissionOutcome> CheckAnalysisStatusAsync(RmsNerisProfile profile, string nerisAnalysisId, CancellationToken cancellationToken = default)
		{
			var credential = await _profiles.GetCredentialAsync(profile);
			return await _client.GetIncidentAnalysisStatusAsync(profile, credential, nerisAnalysisId, cancellationToken);
		}

		private static NerisSubmissionOutcome ValidateQueuedPayload(RmsNerisProfile profile, RmsSubmission submission, bool analysis)
		{
			var errors = new List<NerisSubmissionError>();
			void Add(string path, string message, string code = "neris.local.condition") => errors.Add(new NerisSubmissionError { FieldPath = path, Message = message, Code = code });
			if (profile == null || profile.DepartmentId != submission.DepartmentId)
				Add("/", "The submission profile does not belong to this department.", "neris.local.profile");
			if (string.IsNullOrWhiteSpace(submission.PayloadJson)) Add("/", "The queued submission contains no payload.", "neris.local.payload");
			else
			{
				var schemaIssues = NerisContractCatalog.Instance.Validate(analysis ? "IncidentAnalysisPayload" : "IncidentPayload", submission.PayloadJson, submission.DepartmentId, submission.RecordId);
				foreach (var issue in schemaIssues) Add(issue.FieldPath, issue.Message, "neris.local." + issue.RuleKey);
				if (schemaIssues.Count == 0) NerisPayloadRules.Validate(JObject.Parse(submission.PayloadJson), analysis, (path, message) => Add(path, message));
			}
			if (errors.Count == 0) return null;
			return new NerisSubmissionOutcome
			{
				Kind = NerisOutcomeKind.Rejected, LocalValidationFailure = true,
				Message = "The queued payload failed local contract validation. Nothing was sent to NERIS; correct the report before submitting again.",
				Errors = errors
			};
		}
	}
}

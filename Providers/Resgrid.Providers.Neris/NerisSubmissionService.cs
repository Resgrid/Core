using System;
using System.Threading;
using System.Threading.Tasks;
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
			if (string.IsNullOrWhiteSpace(submission.PayloadJson))
				return new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Fatal, Message = "The submission carries no payload." };

			var credential = await _profiles.GetCredentialAsync(profile);
			if (!string.IsNullOrWhiteSpace(existingNerisIncidentId))
			{
				var update = await _client.UpdateIncidentAsync(profile, credential, existingNerisIncidentId, submission.PayloadJson, cancellationToken);
				if (update.Kind == NerisOutcomeKind.Fatal && update.StatusCode == 404)
					return await _client.CreateIncidentAsync(profile, credential, submission.PayloadJson, cancellationToken);
				return update;
			}

			return await _client.CreateIncidentAsync(profile, credential, submission.PayloadJson, cancellationToken);
		}

		public async Task<NerisSubmissionOutcome> CheckStatusAsync(RmsNerisProfile profile, string nerisIncidentId, CancellationToken cancellationToken = default)
		{
			var credential = await _profiles.GetCredentialAsync(profile);
			return await _client.GetStatusAsync(profile, credential, nerisIncidentId, cancellationToken);
		}
	}
}

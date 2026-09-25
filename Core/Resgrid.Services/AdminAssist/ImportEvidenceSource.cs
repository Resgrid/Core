using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Mailbox polling metadata only. A quiet inbox is not a failed integration.</summary>
	public sealed class ImportEvidenceSource(IDepartmentCallEmailsRepository mailboxes, IDepartmentSettingsService settings) : IAdminAssistEvidenceSource
	{
		public string SourceId => "EmailImportPolling";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "importHeartbeatExpected", "importHeartbeatMissing", "emailImportFailureCount", "emailImportSourceCount" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var profile = await settings.GetOperatingProfileAsync(actor.DepartmentId).WaitAsync(ct) ?? throw new InvalidOperationException();
			Validator.ValidateObject(profile, new ValidationContext(profile), true);
			var rows = (await mailboxes.GetAllByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			if (rows.Count > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000) || rows.Any(r => r.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
			var expected = profile.ExpectedEmailPollIntervalMinutes;
			// Future timestamps cannot establish a fresh poll; absent timestamps remain unobserved.
			var valid = rows.Count > 0 && rows.All(r => r.LastCheck.HasValue && r.LastCheck.Value <= now);
			return new[] {
				new ConfigurationEvidence("importHeartbeatExpected", EvidenceState.Known, SourceId, "email-poll-v1", now, Boolean: expected.HasValue),
				new ConfigurationEvidence("importHeartbeatMissing", !expected.HasValue ? EvidenceState.NotApplicable : valid ? EvidenceState.Known : EvidenceState.Unknown,
					SourceId, "email-poll-v1", now, Boolean: expected.HasValue && valid ? rows.Any(r => now - r.LastCheck.Value > TimeSpan.FromMinutes(expected.Value)) : null,
					ReasonCode: expected.HasValue && !valid ? "PollingNotObserved" : null),
				new ConfigurationEvidence("emailImportFailureCount", EvidenceState.Known, SourceId, "email-poll-v1", now, Number: rows.Count(r => r.IsFailure)),
				new ConfigurationEvidence("emailImportSourceCount", EvidenceState.Known, SourceId, "email-poll-v1", now, Number: rows.Count)
			};
		}
	}
}

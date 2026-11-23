using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>
	/// Push-to-Talk availability (the entitlement gate the v4 Voice API checks before it returns channels) and the configured
	/// standing-channel count (the stored voice record, read whatever the entitlement says). The count is stored configuration,
	/// not what the apps are given: the v4 API exposes no channels while <c>pttAvailable</c> is false, and the rules pair the two
	/// facts (a channel check does not apply without Push-to-Talk). Counts and a boolean only: no channel names, tokens or member
	/// identifiers leave this adapter. Each fact is read independently, so an unavailable billing check does not hide the count.
	/// </summary>
	public sealed class VoiceEvidenceSource(IVoiceService voice) : IAdminAssistEvidenceSource
	{
		public const string PttAvailableId = "pttAvailable";
		public const string VoiceChannelCountId = "voiceChannelCount";
		public string SourceId => "VoiceChannels";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { PttAvailableId, VoiceChannelCountId };

		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var result = new List<ConfigurationEvidence>();
			ConfigurationEvidence Unknown(string id) => new(id, EvidenceState.Unknown, SourceId, "1", now, ReasonCode: "SourceUnavailable");
			ct.ThrowIfCancellationRequested();
			try
			{
				result.Add(new ConfigurationEvidence(PttAvailableId, EvidenceState.Known, SourceId, "1", now,
					Boolean: await voice.CanDepartmentUseVoiceAsync(actor.DepartmentId).WaitAsync(ct)));
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception) { result.Add(Unknown(PttAvailableId)); }
			ct.ThrowIfCancellationRequested();
			try
			{
				// No voice record means Push-to-Talk was never set up for the department: it has no channels. Only standing
				// channels count; an incident commander's on-demand channel closes with its incident.
				var record = await voice.GetVoiceSettingsForDepartmentAsync(actor.DepartmentId).WaitAsync(ct);
				result.Add(record != null && record.DepartmentId != actor.DepartmentId ? Unknown(VoiceChannelCountId) :
					new ConfigurationEvidence(VoiceChannelCountId, EvidenceState.Known, SourceId, "1", now,
						Number: record?.Channels?.Count(c => c != null && !c.IsOnDemand) ?? 0));
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception) { result.Add(Unknown(VoiceChannelCountId)); }
			return result;
		}
	}
}

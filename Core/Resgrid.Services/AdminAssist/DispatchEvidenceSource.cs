using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class DispatchEvidenceSource(IRunCardsService runCards, ICheckInTimerService timers,
		IWeatherAlertService weather, ICommunicationTestService communication) : IAdminAssistEvidenceSource
	{
		public string SourceId => "DispatchConfiguration";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "runCardCount", "checkInTimerCount", "weatherZoneCount", "communicationTestAgeDays" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var result = new List<ConfigurationEvidence>();
			async Task Read(string id, Func<Task<decimal?>> read)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					var number = await read().WaitAsync(ct);
					result.Add(new ConfigurationEvidence(id, number.HasValue ? EvidenceState.Known : EvidenceState.Unknown, SourceId, "1", now,
						Number: number, ReasonCode: number.HasValue ? null : "SourceUnavailable"));
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
				catch (Exception) { result.Add(new ConfigurationEvidence(id, EvidenceState.Unknown, SourceId, "1", now, ReasonCode: "SourceUnavailable")); }
			}
			await Read("runCardCount", async () => (await runCards.GetAllRunCardsForDepartmentAsync(actor.DepartmentId, true))?.Count);
			await Read("checkInTimerCount", async () => (await timers.GetTimerConfigsForDepartmentAsync(actor.DepartmentId))?.Count);
			await Read("weatherZoneCount", async () => (await weather.GetZonesByDepartmentIdAsync(actor.DepartmentId))?.Count);
			await Read("communicationTestAgeDays", async () =>
			{
				var runs = (await communication.GetRunsByDepartmentIdAsync(actor.DepartmentId))?.ToList();
				if (runs == null || runs.Count > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000)) return null;
				// An untargeted, completed run only establishes test recency, never delivery to every member.
				var completed = runs.Where(r => r.DepartmentId == actor.DepartmentId && r.Status == (int)CommunicationTestRunStatus.Completed &&
					r.CompletedOn.HasValue && r.CompletedOn <= now && r.TargetedUserIds?.Trim() == "null").Select(r => r.CompletedOn.Value).ToList();
				return completed.Count == 0 ? 36500m : (decimal)(now - completed.Max()).TotalDays;
			});
			return result;
		}
	}
}

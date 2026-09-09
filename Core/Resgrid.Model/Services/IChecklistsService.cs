using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Services
{
	public interface IChecklistsService
	{
		Task<ChecklistComplianceSummary> GetComplianceSummaryAsync(ChecklistActor actor, ChecklistReportQuery query);
		Task<List<ChecklistReportEntry>> GetEntityChecklistHistoryAsync(ChecklistActor actor, ChecklistTargetType entityType, string entityId, DateTime fromUtc, DateTime untilUtc);
		Task<ReadinessEvidenceManifestV1> GetReadinessPacketForCallAsync(ChecklistActor actor, int callId, int lookbackDays = 30);
		Task<bool> CanManageAsync(ChecklistActor actor);
		Task<List<ChecklistCalendarEntry>> CalendarAsync(ChecklistActor actor, DateTime fromUtc, DateTime untilUtc);
		Task<ChecklistOccurrenceView> OccurrenceAsync(ChecklistActor actor, string id);
		Task<List<ChecklistAssignmentChoice>> AssignmentChoicesAsync(ChecklistActor actor);
		Task<bool> AssetTargetsAvailableAsync(ChecklistActor actor);
		Task<ChecklistReminderSettingsInput> ReminderSettingsAsync(ChecklistActor actor);
		Task SaveReminderSettingsAsync(ChecklistActor actor, ChecklistReminderSettingsInput input);
		Task<List<ChecklistDefinitionView>> ListAsync(ChecklistActor actor, int page = 0, bool includeNext = false);
		Task<ChecklistDefinitionView> GetDefinitionAsync(ChecklistActor actor, string id);
		Task<string> SaveDefinitionAsync(ChecklistActor actor, string id, int revision, ChecklistForm form);
		Task PublishAsync(ChecklistActor actor, string id, int revision);
		Task RetireAsync(ChecklistActor actor, string id, int revision, bool delete = false);
		Task<List<ChecklistTarget>> TargetsAsync(ChecklistActor actor, ChecklistTargetType type);
		Task<string> StartAsync(ChecklistActor actor, string definitionId, string targetId, string completionId);
		Task<string> StartPinnedAsync(ChecklistActor actor, string definitionId, string versionId, string targetId, string completionId);
		Task<string> StartOccurrenceWithIdAsync(ChecklistActor actor, string occurrenceId, string completionId);
		Task<ChecklistRunView> PreviewOccurrenceAsync(ChecklistActor actor, string occurrenceId);
		Task<ChecklistMobilePage> MobileDueAsync(ChecklistActor actor, ChecklistMobileQuery query);
		Task<List<ChecklistHistoryEntry>> MobileHistoryAsync(ChecklistActor actor, ChecklistMobileQuery query);
		Task AddFileAtRevisionAsync(ChecklistActor actor, string id, string itemId, int revision, string fileName, string contentType, byte[] data);
		Task<ChecklistRunView> GetRunAsync(ChecklistActor actor, string id);
		Task<List<ChecklistHistoryEntry>> HistoryAsync(ChecklistActor actor, string definitionId, int page = 0, bool includeNext = false);
		Task<int> SaveRunAsync(ChecklistActor actor, string id, ChecklistRunInput input, bool submit);
		Task WitnessAsync(ChecklistActor actor, string id, string submissionHash, string attestation);
		Task AddFileAsync(ChecklistActor actor, string id, string itemId, string fileName, string contentType, byte[] data);
		Task<ChecklistCompletionFile> GetFileAsync(ChecklistActor actor, string id);
		Task DeleteFileAsync(ChecklistActor actor, string id);
		Task DeleteFileAtRevisionAsync(ChecklistActor actor, string id, int revision);
		Task<List<ChecklistScheduleView>> SchedulesAsync(ChecklistActor actor, string definitionId, int page = 0);
		Task<ChecklistScheduleView> GetScheduleAsync(ChecklistActor actor, string id);
		Task<string> SaveScheduleAsync(ChecklistActor actor, ChecklistScheduleInput input);
		Task DisableScheduleAsync(ChecklistActor actor, string id, int revision);
		Task<List<ChecklistOccurrenceView>> DueAsync(ChecklistActor actor, int page = 0, bool includeNext = false);
		Task<string> StartOccurrenceAsync(ChecklistActor actor, string occurrenceId);
		Task SkipOccurrenceAsync(ChecklistActor actor, string occurrenceId, int revision, string reason);
		Task<ChecklistScheduleSweepResult> SweepSchedulesAsync(DateTime utcNow, CancellationToken ct = default);
	}
}

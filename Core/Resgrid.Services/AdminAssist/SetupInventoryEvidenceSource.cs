using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>
	/// Counts of department configuration catalogs (call types, roles, lists, trainings…) that show whether a key feature
	/// has been set up. Counts only: no names or content leave this adapter. Each count is read independently so one
	/// unavailable owning service reports its own evidence unknown instead of hiding the others.
	/// </summary>
	public sealed class SetupInventoryEvidenceSource(ICallsService calls, IPersonnelRolesService roles, IUnitsService units,
		INotificationService notifications, IDistributionListsService lists, ITrainingService trainings,
		ICertificationService certifications, IChecklistsService checklists, IWorkflowService workflows,
		IProtocolsService protocols) : IAdminAssistEvidenceSource
	{
		public string SourceId => "SetupInventory";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "callTypeCount", "personnelRoleCount", "unitTypeCount", "notificationRuleCount",
			"distributionListCount", "trainingCount", "certificationTypeCount", "checklistCount", "workflowCount", "protocolCount" };

		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var department = actor.DepartmentId;
			var result = new List<ConfigurationEvidence>();
			async Task Count(string id, Func<Task<int?>> read)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					var count = await read().WaitAsync(ct);
					result.Add(count is >= 0
						? new ConfigurationEvidence(id, EvidenceState.Known, SourceId, "1", now, Number: count)
						: new ConfigurationEvidence(id, EvidenceState.Unknown, SourceId, "1", now, ReasonCode: "SourceUnavailable"));
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
				catch (UnauthorizedAccessException) { result.Add(new ConfigurationEvidence(id, EvidenceState.Redacted, SourceId, "1", now, ReasonCode: "SourceAccessUnavailable")); }
				catch (Exception) { result.Add(new ConfigurationEvidence(id, EvidenceState.Unknown, SourceId, "1", now, ReasonCode: "SourceUnavailable")); }
			}
			await Count("callTypeCount", async () => (await calls.GetCallTypesForDepartmentAsync(department))?.Count);
			await Count("personnelRoleCount", async () => (await roles.GetRolesForDepartmentAsync(department))?.Count);
			await Count("unitTypeCount", async () => (await units.GetUnitTypesForDepartmentAsync(department))?.Count);
			await Count("notificationRuleCount", async () => (await notifications.GetNotificationsByDepartmentAsync(department))?.Count);
			await Count("distributionListCount", async () => (await lists.GetDistributionListsByDepartmentIdAsync(department))?.Count);
			await Count("trainingCount", async () => (await trainings.GetAllTrainingsForDepartmentAsync(department))?.Count);
			await Count("certificationTypeCount", async () => (await certifications.GetAllCertificationTypesByDepartmentAsync(department))?.Count);
			// The owning service applies the Checklists flag, module and permission checks for this administrator.
			await Count("checklistCount", async () => (await checklists.ListAsync(new ChecklistActor { DepartmentId = department, UserId = actor.UserId }))?.Count);
			await Count("workflowCount", async () => (await workflows.GetWorkflowsByDepartmentIdAsync(department, ct))?.Count);
			await Count("protocolCount", async () => (await protocols.GetAllProtocolsForDepartmentAsync(department))?.Count);
			return result;
		}
	}
}

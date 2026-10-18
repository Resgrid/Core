using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// The workspace timeline (ImportEvents) both consoles show. Details must be value-free (codes, counts, ids): callers
	/// never pass cell values, file names or staged data (conformance I4). Recording never fails the caller.
	/// </summary>
	public class ImportTimelineService : IImportTimelineService
	{
		private readonly IImportEventsRepository _events;

		public ImportTimelineService(IImportEventsRepository events)
		{
			_events = events;
		}

		public async Task RecordAsync(int departmentId, string workspaceId, ImportActorKind actorKind, string actorIdentity, string action,
			object details = null, string correlationId = null, CancellationToken cancellationToken = default)
		{
			try
			{
				await _events.InsertAsync(new ImportEvent
				{
					DepartmentId = departmentId,
					WorkspaceId = workspaceId,
					ActorKind = (int)actorKind,
					ActorIdentity = actorIdentity,
					Action = action,
					Details = details == null ? null : JsonConvert.SerializeObject(details),
					CorrelationId = correlationId,
					CreatedOn = DateTime.UtcNow
				}, cancellationToken, true);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Import timeline event {action} for workspace {workspaceId} was not recorded", correlationId);
			}
		}

		public Task<List<ImportEvent>> GetAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default) =>
			_events.GetPageAsync(departmentId, workspaceId, skip, Math.Clamp(take, 1, 200), cancellationToken);
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Manages incident-scoped ad-hoc units/personnel and unit forming. Logs creation to the command timeline
	/// via <see cref="IIncidentCommandService"/> (no cycle — IncidentCommandService does not depend on this).
	/// </summary>
	public class IncidentResourcesService : IIncidentResourcesService
	{
		private readonly IIncidentAdHocUnitRepository _adHocUnitRepository;
		private readonly IIncidentAdHocPersonnelRepository _adHocPersonnelRepository;
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly IEventAggregator _eventAggregator;

		public IncidentResourcesService(
			IIncidentAdHocUnitRepository adHocUnitRepository,
			IIncidentAdHocPersonnelRepository adHocPersonnelRepository,
			IIncidentCommandService incidentCommandService,
			IEventAggregator eventAggregator)
		{
			_adHocUnitRepository = adHocUnitRepository;
			_adHocPersonnelRepository = adHocPersonnelRepository;
			_incidentCommandService = incidentCommandService;
			_eventAggregator = eventAggregator;
		}

		#region Ad-hoc units

		public async Task<IncidentAdHocUnit> CreateAdHocUnitAsync(IncidentAdHocUnit unit, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The call must have an active command owned by the caller's department. CallId is an auto-increment
			// integer (guessable), so this prevents creating resources against another department's call.
			var command = await _incidentCommandService.GetActiveCommandForCallAsync(unit.DepartmentId, unit.CallId);
			if (command == null)
				return null;

			// Idempotent create: the client may generate the GUID PK offline. If a row with that id already exists
			// for this department the create was already applied (replay) — return it without duplicating. Otherwise
			// INSERT explicitly; SaveOrUpdateAsync would treat the pre-set GUID as a 0-row UPDATE, not an insert.
			if (!string.IsNullOrWhiteSpace(unit.IncidentAdHocUnitId))
			{
				var stored = await _adHocUnitRepository.GetByIdAsync(unit.IncidentAdHocUnitId);
				if (stored != null)
					return stored.DepartmentId == unit.DepartmentId ? stored : null;
			}
			else
			{
				unit.IncidentAdHocUnitId = Guid.NewGuid().ToString();
			}

			unit.CreatedByUserId = userId;
			if (unit.CreatedOn == default(DateTime))
				unit.CreatedOn = DateTime.UtcNow;

			unit = await _adHocUnitRepository.InsertAsync(Touch(unit), cancellationToken);

			await LogAsync(unit.DepartmentId, unit.CallId, $"Ad-hoc unit '{unit.Name}' created", userId, cancellationToken);

			_eventAggregator.SendMessage<AdHocResourceCreatedEvent>(new AdHocResourceCreatedEvent { DepartmentId = unit.DepartmentId, CallId = unit.CallId, ResourceId = unit.IncidentAdHocUnitId, Name = unit.Name, Kind = "Unit" });
			return unit;
		}

		public async Task<IncidentAdHocUnit> GetAdHocUnitByIdAsync(string incidentAdHocUnitId)
		{
			return await _adHocUnitRepository.GetByIdAsync(incidentAdHocUnitId);
		}

		public async Task<List<IncidentAdHocUnit>> GetAdHocUnitsForCallAsync(int departmentId, int callId)
		{
			var items = await _adHocUnitRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentAdHocUnit>();

			return items.Where(x => x.CallId == callId && x.ReleasedOn == null).ToList();
		}

		public async Task<bool> ReleaseAdHocUnitAsync(int departmentId, string incidentAdHocUnitId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var unit = await _adHocUnitRepository.GetByIdAsync(incidentAdHocUnitId);
			if (unit == null || unit.DepartmentId != departmentId)
				return false;

			unit.ReleasedOn = DateTime.UtcNow;
			await _adHocUnitRepository.SaveOrUpdateAsync(Touch(unit), cancellationToken);

			await LogAsync(unit.DepartmentId, unit.CallId, $"Ad-hoc unit '{unit.Name}' released", userId, cancellationToken);
			return true;
		}

		#endregion Ad-hoc units

		#region Ad-hoc personnel

		public async Task<IncidentAdHocPersonnel> CreateAdHocPersonnelAsync(IncidentAdHocPersonnel personnel, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The call must have an active command owned by the caller's department (guessable integer CallId guard).
			var command = await _incidentCommandService.GetActiveCommandForCallAsync(personnel.DepartmentId, personnel.CallId);
			if (command == null)
				return null;

			// Idempotent create (see CreateAdHocUnitAsync): replay of an existing id returns the stored row; a new id
			// is inserted explicitly (a pre-set GUID + SaveOrUpdateAsync would be a 0-row UPDATE, not an insert).
			if (!string.IsNullOrWhiteSpace(personnel.IncidentAdHocPersonnelId))
			{
				var stored = await _adHocPersonnelRepository.GetByIdAsync(personnel.IncidentAdHocPersonnelId);
				if (stored != null)
					return stored.DepartmentId == personnel.DepartmentId ? stored : null;
			}
			else
			{
				personnel.IncidentAdHocPersonnelId = Guid.NewGuid().ToString();
			}

			personnel.CreatedByUserId = userId;
			if (personnel.CreatedOn == default(DateTime))
				personnel.CreatedOn = DateTime.UtcNow;

			personnel = await _adHocPersonnelRepository.InsertAsync(Touch(personnel), cancellationToken);

			await LogAsync(personnel.DepartmentId, personnel.CallId, $"Ad-hoc personnel '{personnel.Name}' created", userId, cancellationToken);

			_eventAggregator.SendMessage<AdHocResourceCreatedEvent>(new AdHocResourceCreatedEvent { DepartmentId = personnel.DepartmentId, CallId = personnel.CallId, ResourceId = personnel.IncidentAdHocPersonnelId, Name = personnel.Name, Kind = "Personnel" });
			return personnel;
		}

		public async Task<List<IncidentAdHocPersonnel>> GetAdHocPersonnelForCallAsync(int departmentId, int callId)
		{
			var items = await _adHocPersonnelRepository.GetAllByDepartmentIdAsync(departmentId);
			if (items == null)
				return new List<IncidentAdHocPersonnel>();

			return items.Where(x => x.CallId == callId && x.ReleasedOn == null).ToList();
		}

		public async Task<bool> ReleaseAdHocPersonnelAsync(int departmentId, string incidentAdHocPersonnelId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var personnel = await _adHocPersonnelRepository.GetByIdAsync(incidentAdHocPersonnelId);
			if (personnel == null || personnel.DepartmentId != departmentId)
				return false;

			personnel.ReleasedOn = DateTime.UtcNow;
			await _adHocPersonnelRepository.SaveOrUpdateAsync(Touch(personnel), cancellationToken);

			await LogAsync(personnel.DepartmentId, personnel.CallId, $"Ad-hoc personnel '{personnel.Name}' released", userId, cancellationToken);
			return true;
		}

		#endregion Ad-hoc personnel

		#region Roster building

		public async Task<IncidentAdHocPersonnel> AssignPersonnelToUnitAsync(int departmentId, string incidentAdHocPersonnelId, int ridingResourceKind, string ridingResourceId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var personnel = await _adHocPersonnelRepository.GetByIdAsync(incidentAdHocPersonnelId);
			if (personnel == null || personnel.DepartmentId != departmentId)
				return null;

			personnel.RidingResourceKind = ridingResourceKind;
			personnel.RidingResourceId = ridingResourceId;
			personnel = await _adHocPersonnelRepository.SaveOrUpdateAsync(Touch(personnel), cancellationToken);

			await LogAsync(personnel.DepartmentId, personnel.CallId, $"'{personnel.Name}' added to unit roster", userId, cancellationToken);
			return personnel;
		}

		public async Task<IncidentAdHocUnit> FormUnitFromPersonnelAsync(IncidentAdHocUnit unit, List<string> adHocPersonnelIds, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var createdUnit = await CreateAdHocUnitAsync(unit, userId, cancellationToken);

			// Rejected by the create guard (call not owned / no active command for the caller's department).
			if (createdUnit == null)
				return null;

			if (adHocPersonnelIds != null)
			{
				foreach (var personnelId in adHocPersonnelIds)
				{
					var personnel = await _adHocPersonnelRepository.GetByIdAsync(personnelId);
					if (personnel == null || personnel.DepartmentId != createdUnit.DepartmentId)
						continue;

					personnel.RidingResourceKind = (int)ResourceAssignmentKind.AdHocUnit;
					personnel.RidingResourceId = createdUnit.IncidentAdHocUnitId;
					await _adHocPersonnelRepository.SaveOrUpdateAsync(Touch(personnel), cancellationToken);
				}
			}

			await LogAsync(createdUnit.DepartmentId, createdUnit.CallId, $"Unit '{createdUnit.Name}' formed from on-scene personnel", userId, cancellationToken);
			return createdUnit;
		}

		#endregion Roster building

		#region Offline sync

		public async Task<(List<IncidentAdHocUnit> Units, List<IncidentAdHocPersonnel> Personnel)> GetAdHocChangesSinceAsync(int departmentId, DateTime sinceUtc)
		{
			bool Changed(IChangeTracked e) => e.ModifiedOn.HasValue && e.ModifiedOn.Value > sinceUtc;

			var units = await _adHocUnitRepository.GetAllByDepartmentIdAsync(departmentId);
			var personnel = await _adHocPersonnelRepository.GetAllByDepartmentIdAsync(departmentId);

			return (
				units?.Where(Changed).ToList() ?? new List<IncidentAdHocUnit>(),
				personnel?.Where(Changed).ToList() ?? new List<IncidentAdHocPersonnel>());
		}

		#endregion Offline sync

		#region Private helpers

		/// <summary>Stamps the offline-sync change cursor (ModifiedOn) on every insert/update. See offline-first-architecture.md.</summary>
		private static T Touch<T>(T entity) where T : IChangeTracked
		{
			entity.ModifiedOn = DateTime.UtcNow;
			return entity;
		}

		private async Task LogAsync(int departmentId, int callId, string description, string userId, CancellationToken cancellationToken)
		{
			var command = await _incidentCommandService.GetActiveCommandForCallAsync(departmentId, callId);
			if (command == null)
				return;

			await _incidentCommandService.AddLogEntryAsync(command.IncidentCommandId, departmentId, callId, CommandLogEntryType.AdHocResourceCreated, description, userId, cancellationToken);
		}

		#endregion Private helpers
	}
}

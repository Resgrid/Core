using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitStatusAlertsService : IUnitStatusAlertsService
	{
		/// <summary>
		/// How far ahead of the server's clock a board may be when it reports a unit as overdue. Without this, a
		/// board whose clock runs a few seconds fast would be refused an acknowledgement for an alert it is showing.
		/// </summary>
		public static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromSeconds(30);

		private readonly IUnitStatusAlertAcknowledgementsRepository _acknowledgementsRepository;
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;

		public UnitStatusAlertsService(IUnitStatusAlertAcknowledgementsRepository acknowledgementsRepository, IUnitsService unitsService,
			ICustomStateService customStateService, IDepartmentSettingsService departmentSettingsService, IEventAggregator eventAggregator,
			IUnitOfWork unitOfWork)
		{
			_acknowledgementsRepository = acknowledgementsRepository;
			_unitsService = unitsService;
			_customStateService = customStateService;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
		}

		public async Task<List<UnitStatusAlertAcknowledgement>> GetCurrentAcknowledgementsForDepartmentAsync(int departmentId)
		{
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(departmentId);

			// A unit that has never reported a status gets a placeholder with id 0, which no acknowledgement can reference.
			var currentStateIds = states?.Where(x => x != null && x.UnitStateId > 0).Select(x => x.UnitStateId).Distinct().ToList() ?? new List<int>();

			if (!currentStateIds.Any())
				return new List<UnitStatusAlertAcknowledgement>();

			var acknowledgements = await _acknowledgementsRepository.GetActiveForUnitStatesAsync(departmentId, currentStateIds);

			return acknowledgements?.ToList() ?? new List<UnitStatusAlertAcknowledgement>();
		}

		public async Task<UnitStatusAlertAcknowledgementResult> AcknowledgeAsync(int departmentId, int unitId, int unitStateId, UnitStatusAlertLevels level,
			UnitStatusAlertAcknowledgementModes mode, int muteMinutes, string note, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!Enum.IsDefined(typeof(UnitStatusAlertAcknowledgementModes), mode))
				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.InvalidMode);

			if (level != UnitStatusAlertLevels.Warn && level != UnitStatusAlertLevels.Alert)
				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.InvalidLevel);

			note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

			if (note != null && note.Length > UnitStatusAlertAcknowledgement.MaxNoteLength)
				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.NoteTooLong);

			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit == null || unit.DepartmentId != departmentId)
				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.NotFound);

			var state = await _unitsService.GetLastUnitStateByUnitIdAsync(unitId);

			if (state == null || state.UnitStateId <= 0 || state.UnitStateId != unitStateId)
				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.StatusChanged);

			var now = DateTime.UtcNow;
			var thresholds = await _departmentSettingsService.GetUnitStatusThresholdsAsync(departmentId);
			var customState = await _customStateService.GetCustomUnitStateAsync(state);
			var measured = UnitStatusAlertEvaluator.Evaluate(thresholds, customState?.BaseType, state.Timestamp, now.Add(ClockSkewAllowance));

			if (measured == UnitStatusAlertLevels.None)
				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.NotOverdue);

			// The board may lag a level behind (it saw the warning just as the alert fired). Record what was seen, so
			// the alert comes straight back. It may never run a level ahead, or an alert could be silenced before it fires.
			var acknowledgedLevel = (UnitStatusAlertLevels)Math.Min((int)level, (int)measured);

			DateTime? mutedUntil = null;
			if (mode == UnitStatusAlertAcknowledgementModes.Muted && muteMinutes > 0)
				mutedUntil = now.AddMinutes(Math.Min(muteMinutes, UnitStatusAlertAcknowledgement.MaxMuteMinutes));

			var acknowledgement = new UnitStatusAlertAcknowledgement
			{
				UnitStatusAlertAcknowledgementId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UnitId = unitId,
				UnitStateId = unitStateId,
				Level = (int)acknowledgedLevel,
				Mode = (int)mode,
				MutedUntil = mutedUntil,
				Note = note,
				AcknowledgedByUserId = userId,
				AcknowledgedOn = now
			};

			// The clear and the insert are one transaction, so a failed insert puts the earlier acknowledgement back
			// instead of leaving the episode with none.
			await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);

			try
			{
				await ClearActiveAsync(departmentId, unitId, unitStateId, userId, now, cancellationToken);
				await _acknowledgementsRepository.InsertAsync(acknowledgement, cancellationToken);

				_unitOfWork.CommitChanges();
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Rolled back before reading: PostgreSQL refuses every statement in a transaction that has failed.
				_unitOfWork.DiscardChanges();

				// The filtered unique index allows one uncleared row per episode. If another dispatcher's insert got
				// in between our clear and our insert, theirs stands; hand it back so the board can show it.
				var winner = (await _acknowledgementsRepository.GetActiveForUnitStateAsync(departmentId, unitId, unitStateId))?.FirstOrDefault();

				if (winner == null)
					throw;

				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.Conflict, winner);
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			await NotifyAsync(departmentId, unitId);

			return UnitStatusAlertAcknowledgementResult.Ok(acknowledgement);
		}

		public async Task<UnitStatusAlertAcknowledgement> GetAcknowledgementByIdAsync(int departmentId, string acknowledgementId)
		{
			if (string.IsNullOrWhiteSpace(acknowledgementId))
				return null;

			var acknowledgement = await _acknowledgementsRepository.GetByIdAsync(acknowledgementId);

			return acknowledgement != null && acknowledgement.DepartmentId == departmentId ? acknowledgement : null;
		}

		public async Task<UnitStatusAlertAcknowledgementResult> ClearAsync(int departmentId, string acknowledgementId, string userId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var acknowledgement = await GetAcknowledgementByIdAsync(departmentId, acknowledgementId);

			if (acknowledgement == null)
				return UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.NotFound);

			// Clearing twice (two dispatchers, or a retry) is not an error: the end state is the one asked for.
			if (acknowledgement.ClearedOn.HasValue)
				return UnitStatusAlertAcknowledgementResult.Ok(acknowledgement);

			acknowledgement.ClearedOn = DateTime.UtcNow;
			acknowledgement.ClearedByUserId = userId;

			await _acknowledgementsRepository.UpdateAsync(acknowledgement, cancellationToken);
			await NotifyAsync(departmentId, acknowledgement.UnitId);

			return UnitStatusAlertAcknowledgementResult.Ok(acknowledgement);
		}

		private async Task ClearActiveAsync(int departmentId, int unitId, int unitStateId, string userId, DateTime now, CancellationToken cancellationToken)
		{
			var existing = await _acknowledgementsRepository.GetActiveForUnitStateAsync(departmentId, unitId, unitStateId);

			if (existing == null)
				return;

			foreach (var previous in existing)
			{
				previous.ClearedOn = now;
				previous.ClearedByUserId = userId;

				await _acknowledgementsRepository.UpdateAsync(previous, cancellationToken);
			}
		}

		private async Task NotifyAsync(int departmentId, int unitId)
		{
			// The acknowledgement is saved; a board that misses the push still picks it up on its next refresh.
			try
			{
				await _eventAggregator.SendMessageAsync(new UnitStatusAlertUpdatedEvent { DepartmentId = departmentId, UnitId = unitId });
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Unit status alert update could not be published for department {departmentId}, unit {unitId}.");
			}
		}

		/// <summary>PostgreSQL 23505 or SQL Server 2601/2627: the only insert failure that means another dispatcher acknowledged first.</summary>
		private static bool IsUniqueViolation(Exception ex)
		{
			if (ex is Npgsql.PostgresException postgres)
				return postgres.SqlState == "23505";
			if (ex is Microsoft.Data.SqlClient.SqlException sql)
				return sql.Number == 2601 || sql.Number == 2627;
			return false;
		}
	}
}

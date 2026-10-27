using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>The Incident Back Office part of a member's data export (plan §15.4): only the member's own rows.</summary>
	public interface IIncidentBackOfficeGdprExport
	{
		Task<object> BuildAsync(string userId, int departmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The member's participant records and everything hanging off them — position assignments, check-ins, time and lodging. ADP
	/// columns leave the export sealed (shown as REDACTED); the department reveals them on request through its own grant flow.
	/// </summary>
	public sealed class IncidentBackOfficeGdprExport : IIncidentBackOfficeGdprExport
	{
		private readonly IIncidentLedgerRepository<IncidentParticipant> _participants;
		private readonly IIncidentLedgerRepository<IncidentPositionAssignment> _assignments;
		private readonly IIncidentLedgerRepository<IncidentCheckIn> _checkIns;
		private readonly IIncidentLedgerRepository<IncidentTimeRecord> _time;
		private readonly IIncidentLedgerRepository<IncidentLodgingAssignment> _lodging;

		public IncidentBackOfficeGdprExport(IIncidentLedgerRepository<IncidentParticipant> participants, IIncidentLedgerRepository<IncidentPositionAssignment> assignments,
			IIncidentLedgerRepository<IncidentCheckIn> checkIns, IIncidentLedgerRepository<IncidentTimeRecord> time, IIncidentLedgerRepository<IncidentLodgingAssignment> lodging)
		{
			_participants = participants;
			_assignments = assignments;
			_checkIns = checkIns;
			_time = time;
			_lodging = lodging;
		}

		public async Task<object> BuildAsync(string userId, int departmentId, CancellationToken cancellationToken = default)
		{
			var participants = (await _participants.ListAsync(departmentId, new IncidentLedgerQuery { Take = 10000 }.Where("UserId", userId), cancellationToken)).ToList();
			foreach (var participant in participants)
				foreach (var (_, (get, set)) in IncidentBackOfficeProtectedFields.IncidentParticipant)
					set(participant, ProtectedDataEnvelope.SafeDisplay(get(participant)));
			var ids = participants.Select(p => p.IncidentParticipantId).ToList();
			var assignments = (await _assignments.ListAsync(departmentId, new IncidentLedgerQuery { Take = 10000 }.Where("UserId", userId), cancellationToken)).ToList();
			if (ids.Count == 0)
				return new { Participants = participants, Assignments = assignments };
			var checkIns = (await _checkIns.ListAsync(departmentId, new IncidentLedgerQuery { Take = 10000 }.WhereIn("SubjectId", ids), cancellationToken)).ToList();
			var time = (await _time.ListAsync(departmentId, new IncidentLedgerQuery { Take = 10000 }.WhereIn("IncidentParticipantId", ids), cancellationToken)).ToList();
			var lodging = (await _lodging.ListAsync(departmentId, new IncidentLedgerQuery { Take = 10000 }.WhereIn("IncidentParticipantId", ids), cancellationToken)).ToList();
			foreach (var stay in lodging)
				foreach (var (_, (get, set)) in IncidentBackOfficeProtectedFields.IncidentLodgingAssignment)
					set(stay, ProtectedDataEnvelope.SafeDisplay(get(stay)));
			return new { Participants = participants, Assignments = assignments, CheckIns = checkIns, Time = time, Lodging = lodging };
		}
	}
}

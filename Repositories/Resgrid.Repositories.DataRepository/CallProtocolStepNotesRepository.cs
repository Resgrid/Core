using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Step-through notes (M0403): department-scoped reads by id and by an offline event's client step id.</summary>
	public class CallProtocolStepNotesRepository : RmsRepositoryBase<CallProtocolStepNote>, ICallProtocolStepNotesRepository
	{
		public CallProtocolStepNotesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static CallProtocolStepNote AsUtc(CallProtocolStepNote note)
		{
			if (note != null)
				note.CreatedOn = DateTime.SpecifyKind(note.CreatedOn, DateTimeKind.Utc);
			return note;
		}

		public async Task<CallProtocolStepNote> GetByClientStepIdAsync(int departmentId, int callProtocolId, string clientStepId)
		{
			if (departmentId <= 0 || callProtocolId <= 0 || string.IsNullOrWhiteSpace(clientStepId))
				return null;
			return AsUtc(await QueryFirstOrDefaultAsync<CallProtocolStepNote>(
				$"SELECT * FROM {Tbl("CallProtocolStepNotes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallProtocolId")} = {P}CallProtocolId " +
				$"AND {Col("ClientStepId")} = {P}ClientStepId",
				new { DepartmentId = departmentId, CallProtocolId = callProtocolId, ClientStepId = clientStepId }));
		}

		public async Task<List<CallProtocolStepNote>> GetByIdsAsync(int departmentId, IReadOnlyCollection<string> ids)
		{
			var list = (ids ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
			if (departmentId <= 0 || list.Count == 0)
				return new List<CallProtocolStepNote>();
			return (await QueryAsync<CallProtocolStepNote>(
				$"SELECT * FROM {Tbl("CallProtocolStepNotes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("CallProtocolStepNoteId", "Ids")}",
				new { DepartmentId = departmentId, Ids = list.ToArray() })).Select(AsUtc).ToList();
		}
	}
}

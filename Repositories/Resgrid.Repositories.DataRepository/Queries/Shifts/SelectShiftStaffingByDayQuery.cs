using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Shifts
{
	public class SelectShiftStaffingByDayQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectShiftStaffingByDayQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectShiftStaffingByDayQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%SHIFTID%",
																				"%SHIFTDAY%"
																			  },
																 new string[] {
																				"ShiftId",
																				"ShiftDay"
																			  },
																 new string[] {
																				"%SHIFTSTAFFINGSTABLE%",
																				"%SHIFTSTAFFINGPERSONSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.ShiftStaffingsTable,
																				_sqlConfiguration.ShiftStaffingPersonsTable
																 }
																 );

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}

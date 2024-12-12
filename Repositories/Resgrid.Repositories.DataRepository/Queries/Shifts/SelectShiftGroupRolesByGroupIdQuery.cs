using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Shifts
{
	public class SelectShiftGroupRolesByGroupIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectShiftGroupRolesByGroupIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectShiftGroupRolesByGroupIdQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%SHIFTGROUPID%"
																},
																 new string[] {
																				"ShiftGroupId"
																 },
																 new string[] {
																				"%SHIFTGROUPROLESTABLE%",
																				"%PERSONNELROLESTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.ShiftGroupRolesTable,
																				_sqlConfiguration.PersonnelRolesTable
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

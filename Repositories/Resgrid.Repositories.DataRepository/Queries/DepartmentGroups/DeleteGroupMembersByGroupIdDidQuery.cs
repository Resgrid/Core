using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.DepartmentGroups
{
	public class DeleteGroupMembersByGroupIdDidQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteGroupMembersByGroupIdDidQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.DeleteGroupMembersByGroupIdDidQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 _sqlConfiguration.DepartmentGroupMembersTable,
																 _sqlConfiguration.ParameterNotation,
																 new string[]
																 {
																	 "%ID%",
																	 "%DID%"
																 },
																 new string[]
																 {
																	 "GroupId",
																	 "DepartmentId"
																 });

			return query;
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
		{
			throw new NotImplementedException();
		}
	}
}

﻿using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.PersonnelRoles
{
	public class SelectRoleUsersByDidQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRoleUsersByDidQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRoleUsersByDidQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%DID%"
					},
					new string[] {
						"DepartmentId"
					},
					new string[] {
						"%PERSONNELROLEUSERSTABLE%",
						"%PERSONNELROLESTABLE%"
					},
					new string[] {
						_sqlConfiguration.PersonnelRoleUsersTable,
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

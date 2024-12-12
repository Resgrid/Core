using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.Role
{
	public class DeleteQuery : IDeleteQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public DeleteQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.DeleteRoleQuery
                                         .ReplaceDeleteQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                       _sqlConfiguration.RoleTable,
																	   "Id", // TODO: Me Fix -SJ
                                                                       $"{_sqlConfiguration.ParameterNotation}Id");

            return query;
        }

        public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
        {
	        throw new NotImplementedException();
        }
    }
}

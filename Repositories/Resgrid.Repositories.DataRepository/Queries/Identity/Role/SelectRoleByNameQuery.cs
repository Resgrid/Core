using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.Role
{
    public class SelectRoleByNameQuery : ISelectQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public SelectRoleByNameQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }

        public string GetQuery()
        {
            var query = _sqlConfiguration.SelectRoleByNameQuery
                                         .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                 _sqlConfiguration.RoleTable,
                                                                 _sqlConfiguration.ParameterNotation,
                                                                 new string[] { "%NAME%" },
                                                                 new string[] { "Name" });

            return query;
        }

        public string GetQuery<TEntity>() where TEntity : class, IEntity
        {
            throw new System.NotImplementedException();
        }
    }
}

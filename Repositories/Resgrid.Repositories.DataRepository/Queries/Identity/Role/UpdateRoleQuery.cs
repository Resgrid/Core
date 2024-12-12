using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.Role
{
    public class UpdateQuery : IUpdateQuery
    {
        private readonly SqlConfiguration _sqlConfiguration;
        public UpdateQuery(SqlConfiguration sqlConfiguration)
        {
            _sqlConfiguration = sqlConfiguration;
        }
    
        public string GetQuery<TEntity>(TEntity entity)
        {
            var roleProperties = entity.GetColumns(_sqlConfiguration, ignoreIdProperty: true);

            var setFragment = roleProperties.UpdateQuerySetFragment(_sqlConfiguration.ParameterNotation);

            var query = _sqlConfiguration.UpdateRoleQuery
                                         .ReplaceUpdateQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
                                                                       _sqlConfiguration.RoleTable,
                                                                       setFragment,
                                                                       ((IEntity)entity).IdName,
                                                                       $"{_sqlConfiguration.ParameterNotation}Id");

            return query;
        }
    }
}

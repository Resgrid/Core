namespace Resgrid.Model.Repositories.Queries.Contracts
{
    public interface ISelectQuery : IQuery
    {
        string GetQuery();
        string GetQuery<TEntity>() where TEntity : class, IEntity;
    }
}

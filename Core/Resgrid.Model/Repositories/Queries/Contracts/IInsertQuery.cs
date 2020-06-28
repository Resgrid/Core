namespace Resgrid.Model.Repositories.Queries.Contracts
{
    public interface IInsertQuery : IQuery
    {
        string GetQuery<TEntity>(TEntity entity);
    }
}

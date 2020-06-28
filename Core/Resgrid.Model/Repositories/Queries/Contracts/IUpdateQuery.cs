namespace Resgrid.Model.Repositories.Queries.Contracts
{
    public interface IUpdateQuery : IQuery
    {
        string GetQuery<TEntity>(TEntity entity);
    }
}

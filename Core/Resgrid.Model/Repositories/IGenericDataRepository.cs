namespace Resgrid.Model.Repositories
{
	public interface IGenericDataRepository<T> : IRepository<T> where T : class, IEntity
	{
	}
}
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IRepository
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public interface IRepository<T> where T : class, IEntity
	{
		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;IEnumerable&lt;T&gt;&gt;.</returns>
		Task<IEnumerable<T>> GetAllAsync();

		/// <summary>
		/// Gets the by identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;T&gt;.</returns>
		Task<T> GetByIdAsync(object id);

		/// <summary>
		/// Gets all by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;T&gt;&gt;.</returns>
		Task<IEnumerable<T>> GetAllByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Inserts the asynchronous.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;T&gt;.</returns>
		Task<T> InsertAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false);

		/// <summary>
		/// Updates the asynchronous.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;T&gt;.</returns>
		Task<T> UpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false);

		/// <summary>
		/// Deletes the asynchronous.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken);

		/// <summary>
		/// Saves the or update asynchronous.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;T&gt;.</returns>
		Task<T> SaveOrUpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false);

		/// <summary>
		/// Gets all by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;T&gt;&gt;.</returns>
		Task<IEnumerable<T>> GetAllByUserIdAsync(string userId);

		/// <summary>
		/// Deletes the multiple asynchronous.
		/// </summary>
		/// <param name="entity">The entity.</param>
		/// <param name="parentKeyName">Name of the parent key.</param>
		/// <param name="parentKeyId">The parent key identifier.</param>
		/// <param name="ids">The ids.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteMultipleAsync(T entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken);
	}
}

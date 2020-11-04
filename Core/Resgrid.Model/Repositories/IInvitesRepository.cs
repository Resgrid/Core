using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IInvitesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Invite}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Invite}" />
	public interface IInvitesRepository: IRepository<Invite>
	{
		/// <summary>
		/// Gets the invite by code asynchronous.
		/// </summary>
		/// <param name="code">The code.</param>
		/// <returns>Task&lt;Invite&gt;.</returns>
		Task<Invite> GetInviteByCodeAsync(Guid code);

		/// <summary>
		/// Gets the invite by email asynchronous.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <returns>Task&lt;Invite&gt;.</returns>
		Task<Invite> GetInviteByEmailAsync(string emailAddress);
	}
}

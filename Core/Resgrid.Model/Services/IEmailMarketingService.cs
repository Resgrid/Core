using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Keeps the email marketing platform's audience in step with Resgrid: department administrators and managing
	/// members are pushed as contacts, and a user's newsletter choice decides whether they are subscribed. Calls never
	/// throw; a platform failure is logged and reported as false so it cannot break the save that triggered it.
	/// </summary>
	public interface IEmailMarketingService
	{
		/// <summary>False when no email marketing platform is configured. Lets callers skip detecting a change at all.</summary>
		bool IsEnabled { get; }

		/// <summary>Adds or refreshes the user as an administrator (or the managing member) of the department.</summary>
		Task<bool> SyncDepartmentAdministratorAsync(Department department, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Applies the user's newsletter choice. An administrator is added or refreshed with that choice; anyone else is
		/// only updated if the platform already holds them, so a regular member is never added to the audience.
		/// </summary>
		Task<bool> SyncNewsletterPreferenceAsync(string userId, bool doNotReceiveNewsletters, CancellationToken cancellationToken = default);
	}
}

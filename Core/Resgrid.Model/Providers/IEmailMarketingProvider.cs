using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IEmailMarketingProvider
	{
		/// <summary>False when no email marketing platform is configured; every other call is then a no-op.</summary>
		bool IsConfigured { get; }

		/// <summary>Creates the contact, or updates the one matching its user id or email address.</summary>
		Task<bool> UpsertContactAsync(EmailMarketingContact contact, CancellationToken cancellationToken = default);

		/// <summary>True when the platform already holds a contact with this user id or email address.</summary>
		Task<bool> ContactExistsAsync(string userId, string emailAddress, CancellationToken cancellationToken = default);
	}
}

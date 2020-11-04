using System;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Middleware
{
	public class ResgridAuthenticationEvents
	{
		/// <summary>
		/// Gets or sets the on authentication failed.
		/// </summary>
		/// <value>The on authentication failed.</value>
		public Func<AuthenticationFailedContext, Task> OnAuthenticationFailed { get; set; } = context => Task.CompletedTask;

		/// <summary>
		/// Gets or sets the on validate credentials.
		/// </summary>
		/// <value>The on validate credentials.</value>
		public Func<ValidateCredentialsContext, Task> OnValidateCredentials { get; set; } = context => Task.CompletedTask;

		/// <summary>
		/// Authentications the failed.
		/// </summary>
		/// <param name="context">The context.</param>
		/// <returns>Task.</returns>
		public virtual Task AuthenticationFailed(AuthenticationFailedContext context) => OnAuthenticationFailed(context);

		/// <summary>
		/// Validates the credentials.
		/// </summary>
		/// <param name="context">The context.</param>
		/// <returns>Task.</returns>
		public virtual Task ValidateCredentials(ValidateCredentialsContext context) => OnValidateCredentials(context);
	}

}

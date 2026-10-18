using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Resgrid.Model.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Services
{
	/// <summary>
	/// <see cref="IMemberAccountService"/> over ASP.NET Identity's UserManager (registered by the Web, API and worker hosts).
	/// Errors are reduced to Identity's error codes; descriptions, which can echo the email or username, are never kept.
	/// </summary>
	public class MemberAccountService : IMemberAccountService
	{
		private readonly UserManager<IdentityUser> _userManager;

		public MemberAccountService(UserManager<IdentityUser> userManager)
		{
			_userManager = userManager;
		}

		public Task<IdentityUser> FindByIdAsync(string userId) =>
			string.IsNullOrWhiteSpace(userId) ? Task.FromResult<IdentityUser>(null) : _userManager.FindByIdAsync(userId);

		public Task<IdentityUser> FindByEmailAsync(string email) =>
			string.IsNullOrWhiteSpace(email) ? Task.FromResult<IdentityUser>(null) : _userManager.FindByEmailAsync(email);

		public Task<IdentityUser> FindByNameAsync(string userName) =>
			string.IsNullOrWhiteSpace(userName) ? Task.FromResult<IdentityUser>(null) : _userManager.FindByNameAsync(userName);

		public async Task<MemberAccountResult> CreateAsync(IdentityUser user, string password)
		{
			user.SecurityStamp ??= Guid.NewGuid().ToString().ToUpper();
			var result = password == null ? await _userManager.CreateAsync(user) : await _userManager.CreateAsync(user, password);
			return Map(result);
		}

		public async Task<MemberAccountResult> SetEmailConfirmedAsync(IdentityUser user, bool confirmed)
		{
			if (user.EmailConfirmed == confirmed)
				return MemberAccountResult.Ok();
			user.EmailConfirmed = confirmed;
			return Map(await _userManager.UpdateAsync(user));
		}

		public async Task<MemberAccountResult> DeleteAsync(IdentityUser user) => Map(await _userManager.DeleteAsync(user));

		public async Task<MemberAccountResult> AddPasswordAsync(IdentityUser user, string password)
		{
			// Only an account without a password (an imported, invited person) gets one this way; password policy applies.
			if (await _userManager.HasPasswordAsync(user))
				return new MemberAccountResult { Errors = { "UserAlreadyHasPassword" } };
			return Map(await _userManager.AddPasswordAsync(user, password));
		}

		private static MemberAccountResult Map(IdentityResult result) => new MemberAccountResult
		{
			Succeeded = result.Succeeded,
			Errors = result.Errors?.Select(e => e.Code).ToList() ?? new System.Collections.Generic.List<string>()
		};
	}
}

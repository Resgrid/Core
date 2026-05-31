using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Resolves personnel by name within a single department. Backed by the same roster source the
	/// personnel handler uses (<see cref="IUsersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync"/>),
	/// so a member is only ever resolvable inside their own department (security addendum §3).
	/// </summary>
	public class ChatbotUserSearchService : IChatbotUserSearchService
	{
		private readonly IUsersService _usersService;

		public ChatbotUserSearchService(IUsersService usersService)
		{
			_usersService = usersService;
		}

		public async Task<List<ChatbotUserMatch>> SearchPersonnelAsync(int departmentId, string query, int max = 15)
		{
			var users = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(departmentId, false, false, false);
			if (users == null || users.Count == 0)
				return new List<ChatbotUserMatch>();

			var filtered = users.AsEnumerable();
			if (!string.IsNullOrWhiteSpace(query))
			{
				var q = query.Trim().ToLowerInvariant();
				filtered = users.Where(u => MatchesQuery(u, q));
			}

			return filtered
				.OrderBy(u => u.LastName)
				.ThenBy(u => u.FirstName)
				.Take(max)
				.Select(ToMatch)
				.ToList();
		}

		public async Task<ChatbotUserMatch> ResolveSingleAsync(int departmentId, string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return null;

			var matches = await SearchPersonnelAsync(departmentId, query, 5);
			if (matches.Count == 0)
				return null;

			// An exact full-name match wins outright.
			var exact = matches
				.Where(m => string.Equals(m.FullName?.Trim(), query.Trim(), StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (exact.Count == 1)
				return exact[0];

			// A single overall match is unambiguous; anything else is ambiguous (caller decides messaging).
			return matches.Count == 1 ? matches[0] : null;
		}

		private static bool MatchesQuery(UserGroupRole u, string lowerQuery)
		{
			return (u.FirstName != null && u.FirstName.ToLowerInvariant().Contains(lowerQuery))
				|| (u.LastName != null && u.LastName.ToLowerInvariant().Contains(lowerQuery))
				|| (u.Name != null && u.Name.ToLowerInvariant().Contains(lowerQuery));
		}

		private static ChatbotUserMatch ToMatch(UserGroupRole u) => new ChatbotUserMatch
		{
			UserId = u.UserId,
			FirstName = u.FirstName,
			LastName = u.LastName,
			FullName = u.Name
		};
	}
}

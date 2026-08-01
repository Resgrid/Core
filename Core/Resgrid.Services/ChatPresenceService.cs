using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Cache-backed chat presence. One key per (department, user) refreshed on connect/heartbeat and
	/// left to expire on disconnect — deliberately eventually-consistent (within PresenceTtlSeconds)
	/// to avoid per-connection bookkeeping across hosts.
	/// </summary>
	public class ChatPresenceService : IChatPresenceService
	{
		private readonly ICacheProvider _cacheProvider;

		public ChatPresenceService(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public async Task<bool> SetOnlineAsync(int departmentId, string userId)
		{
			var key = GetKey(departmentId, userId);
			var existing = await _cacheProvider.GetStringAsync(key);

			await _cacheProvider.SetStringAsync(key, "1", GetTtl());

			return string.IsNullOrWhiteSpace(existing);
		}

		public async Task TouchAsync(int departmentId, string userId)
		{
			await _cacheProvider.SetStringAsync(GetKey(departmentId, userId), "1", GetTtl());
		}

		public async Task<bool> IsOnlineAsync(int departmentId, string userId)
		{
			return !string.IsNullOrWhiteSpace(await _cacheProvider.GetStringAsync(GetKey(departmentId, userId)));
		}

		public async Task<List<string>> GetOnlineUsersAsync(int departmentId, List<string> userIds)
		{
			var online = new List<string>();

			if (userIds == null || userIds.Count == 0)
				return online;

			// No batch/MGET on ICacheProvider: bound-parallel per-user GETs instead of a sequential loop.
			using (var throttler = new SemaphoreSlim(8))
			{
				async Task LookupAsync(string userId)
				{
					await throttler.WaitAsync();
					try
					{
						if (await IsOnlineAsync(departmentId, userId))
							lock (online)
								online.Add(userId);
					}
					finally
					{
						throttler.Release();
					}
				}

				var lookups = new List<Task>();
				foreach (var userId in userIds)
					lookups.Add(LookupAsync(userId));

				await Task.WhenAll(lookups);
			}

			return online;
		}

		private static string GetKey(int departmentId, string userId)
		{
			return $"chatpresence:{departmentId}:{userId?.ToLowerInvariant()}";
		}

		private static TimeSpan GetTtl()
		{
			return TimeSpan.FromSeconds(Math.Max(15, ChatConfig.PresenceTtlSeconds));
		}
	}
}

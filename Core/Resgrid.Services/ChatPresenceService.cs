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

			// Keep the active-channel marker (and its unit mirror) alive across heartbeats so an open
			// conversation stays "active" without the client re-invoking SetActiveChannel.
			var active = await _cacheProvider.GetStringAsync(GetActiveKey(departmentId, userId));
			if (!string.IsNullOrWhiteSpace(active))
			{
				await _cacheProvider.SetStringAsync(GetActiveKey(departmentId, userId), active, GetTtl());

				var unitId = ParseUnitId(active);
				if (unitId.HasValue)
					await _cacheProvider.SetStringAsync(GetUnitActiveKey(departmentId, unitId.Value), ParseChannelId(active), GetTtl());
			}
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

		public async Task SetActiveChannelAsync(int departmentId, string userId, string channelId, int? unitId = null)
		{
			var activeKey = GetActiveKey(departmentId, userId);
			var existingUnitId = ParseUnitId(await _cacheProvider.GetStringAsync(activeKey));

			if (string.IsNullOrWhiteSpace(channelId))
			{
				await _cacheProvider.RemoveAsync(activeKey);
				if (existingUnitId.HasValue)
					await _cacheProvider.RemoveAsync(GetUnitActiveKey(departmentId, existingUnitId.Value));
				return;
			}

			// Acting unit changed (or dropped): clear the stale unit marker so the old rig isn't suppressed.
			if (existingUnitId.HasValue && existingUnitId != unitId)
				await _cacheProvider.RemoveAsync(GetUnitActiveKey(departmentId, existingUnitId.Value));

			var value = unitId.HasValue ? $"{channelId}|{unitId.Value}" : channelId;
			await _cacheProvider.SetStringAsync(activeKey, value, GetTtl());

			if (unitId.HasValue)
				await _cacheProvider.SetStringAsync(GetUnitActiveKey(departmentId, unitId.Value), channelId, GetTtl());
		}

		public async Task ClearActiveChannelAsync(int departmentId, string userId)
		{
			await SetActiveChannelAsync(departmentId, userId, null);
		}

		public async Task<List<string>> GetUsersActiveInChannelAsync(int departmentId, List<string> userIds, string channelId)
		{
			var active = new List<string>();

			if (userIds == null || userIds.Count == 0 || string.IsNullOrWhiteSpace(channelId))
				return active;

			using (var throttler = new SemaphoreSlim(8))
			{
				async Task LookupAsync(string userId)
				{
					await throttler.WaitAsync();
					try
					{
						var value = await _cacheProvider.GetStringAsync(GetActiveKey(departmentId, userId));
						if (string.Equals(ParseChannelId(value), channelId, StringComparison.OrdinalIgnoreCase))
							lock (active)
								active.Add(userId);
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

			return active;
		}

		public async Task<bool> IsUnitActiveInChannelAsync(int departmentId, int unitId, string channelId)
		{
			if (unitId <= 0 || string.IsNullOrWhiteSpace(channelId))
				return false;

			var value = await _cacheProvider.GetStringAsync(GetUnitActiveKey(departmentId, unitId));
			return string.Equals(value, channelId, StringComparison.OrdinalIgnoreCase);
		}

		// Active markers store "channelId" or "channelId|unitId" when the viewer is acting as a unit.
		private static string ParseChannelId(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var separator = value.IndexOf('|');
			return separator < 0 ? value : value.Substring(0, separator);
		}

		private static int? ParseUnitId(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var separator = value.IndexOf('|');
			if (separator < 0 || separator >= value.Length - 1)
				return null;

			return int.TryParse(value.Substring(separator + 1), out var unitId) ? unitId : (int?)null;
		}

		private static string GetKey(int departmentId, string userId)
		{
			return $"chatpresence:{departmentId}:{userId?.ToLowerInvariant()}";
		}

		private static string GetActiveKey(int departmentId, string userId)
		{
			return $"chatactive:{departmentId}:{userId?.ToLowerInvariant()}";
		}

		private static string GetUnitActiveKey(int departmentId, int unitId)
		{
			return $"chatactiveunit:{departmentId}:{unitId}";
		}

		private static TimeSpan GetTtl()
		{
			return TimeSpan.FromSeconds(Math.Max(15, ChatConfig.PresenceTtlSeconds));
		}
	}
}

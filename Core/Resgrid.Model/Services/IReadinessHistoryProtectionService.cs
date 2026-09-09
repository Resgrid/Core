using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IReadinessHistoryProtectionService
	{
		Task ProtectAsync<T>(int departmentId, string rowKey, T entity, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> fields, CancellationToken ct = default) where T : class;
		Task<T> ForDisplayAsync<T>(int departmentId, T entity, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> fields) where T : class;
	}
}

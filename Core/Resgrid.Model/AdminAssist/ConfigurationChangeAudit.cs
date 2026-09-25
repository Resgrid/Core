using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Fingerprint is request-local and must never be persisted. Values contains reviewed scalar/presence markers only.</summary>
	public sealed record ConfigurationChangeStamp(string Fingerprint, string Values);
	public interface IConfigurationChangeJournal
	{
		Task<T> ExecuteAsync<T>(int departmentId, string binding, Func<Task<ConfigurationChangeStamp>> read,
			Func<Task<T>> write, CancellationToken cancellationToken);
	}
}

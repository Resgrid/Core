using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Fail-closed engine placeholder until the broker-backed engine ships (ADP Phase 2/3). Every
	/// night run reports Failed with the value-free code "engine_unavailable" — the coordinator marks
	/// the migration Failed at its cursor, releases the lock, and notifies, exactly as it would for
	/// any other unrecoverable error. Verification never passes, so no department can reach Enabled
	/// (or Disabled from an offboarding) without real, verified data movement.
	/// </summary>
	public class NullDepartmentDataMigrationEngine : IDepartmentDataMigrationEngine
	{
		public const string EngineUnavailableErrorCode = "engine_unavailable";

		public bool IsAvailable => false;

		public Task<AdpMigrationNightResult> RunEncryptionNightAsync(AdpMigrationNightContext context, CancellationToken cancellationToken) =>
			Task.FromResult(AdpMigrationNightResult.Failed(EngineUnavailableErrorCode));

		public Task<AdpMigrationNightResult> RunDecryptionNightAsync(AdpMigrationNightContext context, CancellationToken cancellationToken) =>
			Task.FromResult(AdpMigrationNightResult.Failed(EngineUnavailableErrorCode));

		public Task<bool> VerifyAsync(AdpMigrationNightContext context, CancellationToken cancellationToken) =>
			Task.FromResult(false);
	}
}

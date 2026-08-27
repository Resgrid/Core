using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The bulk field-crypto engine behind the ADP migration worker (plan sections 19.3–19.4): walks
	/// the protected-field catalog table by table in bounded, transactional batches with durable
	/// cursors, enforcing the double-encryption guard (encrypt refuses matching rgdp envelopes and
	/// hard-errors on foreign ones; decrypt passes plaintext through and counts the anomaly). All
	/// crypto goes through the Protected Data Broker with a migration-scoped workload audience —
	/// never a user grant, never plaintext staging tables/files. The coordinator
	/// (AdpMigrationLogic) owns scheduling, the department lock, state transitions and
	/// notifications; the engine owns only data movement and verification scans.
	/// </summary>
	public interface IDepartmentDataMigrationEngine
	{
		/// <summary>True when a real engine (broker-backed) is available on this host.</summary>
		bool IsAvailable { get; }

		/// <summary>Runs one enrollment (encryption) night for the department, up to the window close.</summary>
		Task<AdpMigrationNightResult> RunEncryptionNightAsync(AdpMigrationNightContext context, CancellationToken cancellationToken);

		/// <summary>Runs one offboarding (decryption) night for the department, up to the window close.</summary>
		Task<AdpMigrationNightResult> RunDecryptionNightAsync(AdpMigrationNightContext context, CancellationToken cancellationToken);

		/// <summary>
		/// Post-run verification: counts, AEAD/AAD spot checks, catalog coverage, and the
		/// plaintext-residue scan (enrollment) or envelope-residue scan (offboarding). Only a true
		/// result may transition the department out of Verifying.
		/// </summary>
		Task<bool> VerifyAsync(AdpMigrationNightContext context, CancellationToken cancellationToken);
	}
}

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Resgrid.Config;
using Resgrid.Framework;

namespace Resgrid.Providers.ProtectedData
{
	/// <summary>
	/// A PKCS#12 certificate on disk that is re-read when it changes, so a renewal reaches the next TLS handshake
	/// without a restart.
	///
	/// The broker's certificates come from step-ca through a sidecar that renews short-lived leaves (24 hours by
	/// default, renewed at about two thirds of their life) and replaces the file by atomic rename. Loading the file
	/// once at startup, as Kestrel and a static ClientCertificates collection both do, ignores every renewal and the
	/// broker fails within a day.
	///
	/// <see cref="Current"/> sits on the handshake path, so the file is stat-ed at most once per poll interval and
	/// re-read only when its last-write time moved. A failed reload (the sidecar stopped, a bad write) keeps the
	/// certificate already in hand: it stays valid until it expires, and the broker's readiness check reports one
	/// that is close to expiry. The replaced certificate is never disposed, because a handshake in flight may still
	/// hold it; renewals happen a few times a day, so leaving it to the finalizer costs nothing measurable.
	///
	/// Lives in the ProtectedData provider project because both of its users reference that project already: the
	/// OpenBao client (<see cref="OpenBaoClientCertificate"/>) and the broker host's HTTPS listener. Hosts that do
	/// not load certificates from disk never construct one.
	/// </summary>
	public class ReloadingPkcs12Certificate
	{
		private const int DefaultPollSeconds = 60;
		private const int MaximumPollSeconds = 3600;

		private readonly string _password;
		private readonly TimeSpan _pollInterval;
		private readonly TimeProvider _timeProvider;
		private readonly object _gate = new object();

		private X509Certificate2 _current;
		private DateTime _loadedWriteTimeUtc;
		private DateTime? _failedWriteTimeUtc;
		private DateTimeOffset _nextPollUtc;

		/// <summary>
		/// Loads the certificate immediately and throws if that first load fails, so a missing or unreadable file stops
		/// the host at startup instead of failing every handshake later.
		/// </summary>
		/// <param name="path">PKCS#12 file. The path is configuration; the file is a mounted secret.</param>
		/// <param name="password">PKCS#12 password; null or empty for none.</param>
		/// <param name="pollInterval">Minimum time between checks of the file. Zero checks on every read.</param>
		/// <param name="timeProvider">Clock for the poll interval.</param>
		public ReloadingPkcs12Certificate(string path, string password, TimeSpan pollInterval, TimeProvider timeProvider)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A certificate path is required.", nameof(path));
			if (pollInterval < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(pollInterval), "The poll interval cannot be negative.");

			Path = path;
			_password = string.IsNullOrEmpty(password) ? null : password;
			_pollInterval = pollInterval;
			_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

			// Stat before reading: if the file is replaced between the two, the next poll sees a newer time and reads
			// it again. Reading first could pair the new time with the old contents and miss the renewal.
			var writeTimeUtc = WriteTimeUtc(Path);
			try
			{
				_current = Load(Path, _password);
			}
			catch (Exception ex) when (IsLoadFailure(ex))
			{
				throw new InvalidOperationException($"The certificate at '{Path}' could not be loaded ({ex.GetType().Name}: {ex.Message}).", ex);
			}

			_loadedWriteTimeUtc = writeTimeUtc;
			_nextPollUtc = _timeProvider.GetUtcNow() + _pollInterval;
		}

		/// <summary>The PKCS#12 file this instance reads.</summary>
		public string Path { get; }

		/// <summary>
		/// The poll interval from <see cref="DataProtectionConfig.BrokerCertificatePollSeconds"/>: 60 seconds when unset
		/// or below 1, at most an hour.
		/// </summary>
		public static TimeSpan ConfiguredPollInterval
		{
			get
			{
				var seconds = DataProtectionConfig.BrokerCertificatePollSeconds;
				if (seconds < 1)
					seconds = DefaultPollSeconds;

				return TimeSpan.FromSeconds(Math.Min(seconds, MaximumPollSeconds));
			}
		}

		/// <summary>
		/// The certificate to present on a new handshake. Never null: the constructor guarantees a first load, and a
		/// failed reload keeps the previous certificate.
		/// </summary>
		public X509Certificate2 Current
		{
			get
			{
				var now = _timeProvider.GetUtcNow();
				lock (_gate)
				{
					if (now < _nextPollUtc)
						return _current;

					_nextPollUtc = now + _pollInterval;

					// A missing file reads as 1601-01-01, which differs from the loaded time, so it falls into the
					// failed-load branch below and keeps the current certificate.
					var writeTimeUtc = WriteTimeUtc(Path);
					if (writeTimeUtc == _loadedWriteTimeUtc)
						return _current;

					X509Certificate2 loaded;
					try
					{
						loaded = Load(Path, _password);
					}
					catch (Exception ex) when (IsLoadFailure(ex))
					{
						// Log once per file version, not once per poll: a stopped sidecar would otherwise log every
						// minute until the certificate expires. The readiness check carries the ongoing signal.
						if (writeTimeUtc != _failedWriteTimeUtc)
						{
							_failedWriteTimeUtc = writeTimeUtc;
							Logging.LogError(
								$"The certificate at '{Path}' changed but could not be reloaded ({ex.GetType().Name}); still presenting the certificate valid through {_current.NotAfter.ToUniversalTime():u}.");
						}

						return _current;
					}

					_current = loaded;
					_loadedWriteTimeUtc = writeTimeUtc;
					_failedWriteTimeUtc = null;

					Logging.LogInfo(
						$"Reloaded the certificate at '{Path}' ({loaded.Subject}), valid through {loaded.NotAfter.ToUniversalTime():u}.");

					// The replaced certificate is deliberately not disposed (see the class remarks).
					return _current;
				}
			}
		}

		private static X509Certificate2 Load(string path, string password) =>
			// Default key storage, as before this class existed: Linux keeps PKCS#12 keys in memory either way, and
			// Windows SChannel cannot present an ephemeral-key certificate on a TLS handshake.
			X509CertificateLoader.LoadPkcs12FromFile(path, password);

		private static DateTime WriteTimeUtc(string path)
		{
			try
			{
				return File.GetLastWriteTimeUtc(path);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				// Unreadable metadata counts as "changed": the load that follows fails the same way and keeps the
				// current certificate, so a stat error never fails a handshake on its own.
				return DateTime.MinValue;
			}
		}

		private static bool IsLoadFailure(Exception ex) =>
			ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException;
	}
}

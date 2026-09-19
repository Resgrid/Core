using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Worker 34 (Workforce &amp; Business Operations plan D5), hourly tick. Each department runs its nightly sweep on the
	/// first tick at or after CertificationConfig.SweepLocalHour in its own time zone, once per local day: the day is
	/// claimed in the department's settings row before the sweep runs, so a tick that lands late (drift past the hour),
	/// an hour that repeats or vanishes on a daylight-saving change and an overlapping tick all see one sweep, and
	/// "today", the lead days and the grace deadlines are all department-local dates. A failed sweep gives the claim
	/// back so the next tick of the same day retries it. Only departments with typed personnel records, unit records or
	/// role requirements are visited; one department's failure never stops the others.
	/// </summary>
	public sealed class CertificationExpiryLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			if (!Resgrid.Config.CertificationConfig.SweepEnabled)
				return Tuple.Create(true, "Certification sweep disabled.");

			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var certifications = scope.Resolve<ICertificationService>();
				var departments = scope.Resolve<IDepartmentsService>();
				var nowUtc = DateTime.UtcNow;
				var targetHour = Math.Clamp(Resgrid.Config.CertificationConfig.SweepLocalHour, 0, 23);
				int swept = 0, skipped = 0, failed = 0, expired = 0, expiring = 0, removed = 0;

				foreach (var departmentId in await certifications.GetDepartmentsForSweepAsync())
				{
					ct.ThrowIfCancellationRequested();
					try
					{
						var department = await departments.GetDepartmentByIdAsync(departmentId, false);
						if (department == null) { skipped++; continue; }
						var local = nowUtc.TimeConverter(department);
						if (local.Hour < targetHour) { skipped++; continue; }
						if (!await certifications.TryClaimSweepDayAsync(departmentId, local.Date, ct)) { skipped++; continue; }

						try
						{
							var result = await certifications.RunExpirySweepAsync(departmentId, local.Date, ct);
							swept++;
							expired += result.Expired + result.UnitsExpired;
							expiring += result.ExpiringNotified + result.UnitsExpiringNotified;
							removed += result.Removed;
						}
						catch
						{
							await certifications.ReleaseSweepDayAsync(departmentId, local.Date, CancellationToken.None);
							throw;
						}
					}
					catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
					catch (Exception ex)
					{
						failed++;
						Resgrid.Framework.Logging.LogException(ex, $"Certification sweep failed for department {departmentId}.");
					}
				}

				return Tuple.Create(true, $"Certification sweep: swept={swept} skipped={skipped} failed={failed} expired={expired} expiring={expiring} removed={removed}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "Certification expiry worker failed.");
				return Tuple.Create(false, "Certification expiry sweep failed.");
			}
		}
	}
}

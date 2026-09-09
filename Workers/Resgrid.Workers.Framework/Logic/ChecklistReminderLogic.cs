using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	public sealed class ChecklistReminderLogic
	{
		public async Task<Tuple<bool, string>> Process(CancellationToken ct)
		{
			try
			{
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var result = await scope.Resolve<IChecklistReminderService>().SweepAsync(DateTime.UtcNow, ct);
				return Tuple.Create(result.Errors == 0, $"Checklist reminders: handedOff={result.HandedOff}, suppressed={result.Suppressed}, errors={result.Errors}");
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch { return Tuple.Create(false, "Checklist reminders failed."); }
		}
	}
}

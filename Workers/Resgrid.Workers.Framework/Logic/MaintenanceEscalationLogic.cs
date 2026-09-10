using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
namespace Resgrid.Workers.Framework.Logic
{
    public sealed class MaintenanceEscalationLogic
    {
        public async Task<Tuple<bool,string>> Process(CancellationToken ct)
        {
            var errors = 0; var generated = 0; var escalated = 0;
            try
            {
                var after = 0;
                while (true)
                {
                    using var listing = Bootstrapper.GetKernel().BeginLifetimeScope();
                    var departments = await listing.Resolve<IWorkOrderMaintenanceRepository>().MaintenanceDepartmentsAsync(after);
                    foreach (var id in departments)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
                        try { var result = await scope.Resolve<IWorkOrderMaintenanceService>().EscalateMaintenanceAsync(id); errors += result.Errors; generated += result.Generated; escalated += result.Escalated; }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception ex) { errors++; Resgrid.Framework.Logging.LogError($"Maintenance escalation failed for department {id}: {ex.GetType().FullName}."); }
                        after = id;
                    }
                    if (departments.Count < 200) break;
                }
                return Tuple.Create(errors == 0, $"Maintenance: generated={generated}, escalated={escalated}, errors={errors}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { Resgrid.Framework.Logging.LogError($"Maintenance escalation worker failed: {ex.GetType().FullName}."); return Tuple.Create(false, "Maintenance sweep failed."); }
        }
    }
}

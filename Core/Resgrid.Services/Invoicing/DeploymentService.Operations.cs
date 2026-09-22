using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;

namespace Resgrid.Services.Invoicing
{
    public partial class DeploymentService
    {
        public async Task<Deployment> SynchronizeExternalOrderAsync(string orderId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
        {
            var deployment = await GetDeploymentByExternalOrderIdAsync(orderId, departmentId);
            if (deployment == null || deployment.IsDeleted || !deployment.IsOpen) return deployment;
            var source = await _recordDeployments.GetAsync(departmentId, userId, orderId);
            if (source == null) throw new InvalidOperationException("deployments_external_order_not_found");

            // Only accepted, currently deployed resources become active roster entries. Historical entries stay intact.
            // A stale row (unit gone, member left the department) is logged and skipped like the initial prefill, so it
            // cannot hold back the later fills or the status reconciliation below.
            var seatable = source.Fills.Where(f => !f.DeletedOn.HasValue && f.Status >= (int)RmsDeploymentFillStatus.Accepted &&
                f.Status <= (int)RmsDeploymentFillStatus.Assigned && f.Status != (int)RmsDeploymentFillStatus.Declined).ToList();
            foreach (var unitId in seatable.Where(f => f.AssignedUnitId.HasValue).Select(f => f.AssignedUnitId.Value).Distinct())
                if (!deployment.Units.Any(u => u.UnitId == unitId && u.IsActive))
                {
                    try { await AddUnitAsync(deployment.DeploymentId, departmentId, unitId, null, null, userId, ipAddress, userAgent, cancellationToken); }
                    catch (InvalidOperationException ex) { Logging.LogError($"External order {orderId}: unit {unitId} not seated ({ex.Message})."); }
                }
            deployment = await GetDeploymentByIdAsync(deployment.DeploymentId, departmentId);
            foreach (var fill in seatable.Where(f => !string.IsNullOrWhiteSpace(f.AssignedUserId)))
                if (!deployment.Personnel.Any(p => p.IsActive && p.UserId == fill.AssignedUserId))
                {
                    try
                    {
                        await AddPersonnelAsync(deployment.DeploymentId, departmentId, new DeploymentPersonnelInput
                        {
                            UserId = fill.AssignedUserId, CertificationCode = fill.Position, RmsExternalOrderFillId = fill.RmsExternalOrderFillId,
                            DeploymentUnitId = deployment.Units.FirstOrDefault(u => u.IsActive && u.UnitId == fill.AssignedUnitId)?.DeploymentUnitId
                        }, userId, ipAddress, userAgent, cancellationToken);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Logging.LogError($"External order {orderId}: fill {fill.RmsExternalOrderFillId} not seated ({ex.Message}).");
                        continue;
                    }
                    deployment = await GetDeploymentByIdAsync(deployment.DeploymentId, departmentId);
                }

            var active = source.Fills.Where(f => !f.DeletedOn.HasValue && f.Status != (int)RmsDeploymentFillStatus.Declined).ToList();
            var status = source.Order.Status == (int)RmsExternalOrderStatus.ClosedOut ? DeploymentStatuses.Completed
                : active.Count > 0 && active.All(f => f.Status >= (int)RmsDeploymentFillStatus.Released) ? DeploymentStatuses.Demobilizing
                : active.Any(f => f.Status >= (int)RmsDeploymentFillStatus.Mobilized) ? DeploymentStatuses.Active
                : active.Any(f => f.Status == (int)RmsDeploymentFillStatus.Accepted) ? DeploymentStatuses.Standby : DeploymentStatuses.Planned;
            return (int)status > deployment.Status
                ? await SetDeploymentStatusAsync(deployment.DeploymentId, departmentId, status, userId, ipAddress, userAgent, cancellationToken)
                : deployment;
        }
    }
}

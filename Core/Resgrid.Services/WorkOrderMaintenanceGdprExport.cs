using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public partial class GdprDataExportService
    {
        private readonly IWorkOrderMaintenanceRepository _workOrderMaintenance;
        private async Task<object> BuildMaintenanceDataAsync(string userId, int departmentId)
        {
            async Task<List<T>> Rows<T>(Func<T, bool> subject) where T : WorkOrderRow
            {
                var result = new List<T>();
                for (var skip = 0; ; skip += 500)
                {
                    var batch = await _workOrderMaintenance.QueryMaintenanceAsync<T>(departmentId, skip: skip);
                    foreach (var row in batch.Where(subject))
                        result.Add(await _checklistProtection.Value.ForDisplayAsync(departmentId, row, WorkOrderTables.Fields<T>()));
                    if (batch.Count < 500) return result;
                }
            }
            // Export subject-authored facts only; holding a maintenance role does not imply ownership of others' histories.
            var versions = await Rows<WorkOrderRecurrenceVersion>(r => r.CreatedBy == userId);
            var readings = await Rows<WorkOrderMeterReading>(r => r.CreatedBy == userId);
            var changes = await Rows<WorkOrderRecurrenceChange>(r => r.CreatedBy == userId);
            var related = versions.Select(r => r.RecurrenceId).Concat(readings.Select(r => r.RecurrenceId)).Concat(changes.Select(r => r.RecurrenceId)).ToHashSet();
            return new {
                FailureIntents = await Rows<WorkOrderFailureIntent>(r => r.CreatedBy == userId || r.AuthorizedBy == userId),
                SafetyHolds = await Rows<WorkOrderSafetyHold>(r => r.CreatedBy == userId || r.ReleasedBy == userId),
                Recurrences = await Rows<WorkOrderRecurrence>(r => r.CreatedBy == userId || r.AssignedToUserId == userId || related.Contains(r.Id)),
                Versions = versions, Readings = readings, Changes = changes
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Resources;
using System.Text;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public static class WorkOrderReportDocuments
    {
        private static readonly ResourceManager Strings = new("Resgrid.Localization.Areas.User.WorkOrders.WorkOrders", typeof(Resgrid.Localization.SupportedLocales).Assembly);
        private static string Text(string key) => Strings.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        private static string CsvCell(object value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n')) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        public static byte[] Csv(IEnumerable<WorkOrderReportEntry> entries)
        {
            var csv = new StringBuilder(string.Join(",", new[] { "Number", "Title", "Status", "Priority", "Unit", "Asset", "CreatedOn", "StartedOn", "CompletedOn", "RepairHours", "ActiveRepairHours", "WaitingHours", "DowntimeHours", "ResponseDueOn", "RepairDueOn", "Currency", "Labor", "Parts", "VendorCharges", "TotalCost", "UnknownLabor", "UnknownParts" }.Select(k => CsvCell(Text(k)))) + "\r\n");
            foreach (var entry in entries)
                foreach (var cost in entry.Costs.DefaultIfEmpty(new WorkOrderCostTotal()))
                    csv.AppendLine(string.Join(",", new object[] { entry.Order.Number, entry.Order.Title, Text("Status" + entry.Order.Status), Text("Priority" + entry.Order.Priority), entry.Order.UnitId, entry.Order.AssetId,
                        entry.Order.CreatedOn.ToString("O"), entry.StartedOn?.ToString("O"), entry.CompletedOn?.ToString("O"), entry.RepairHours, entry.ActiveRepairHours, entry.WaitingHours, entry.DowntimeHours, entry.ResponseDueOn?.ToString("O"), entry.RepairDueOn?.ToString("O"), cost.Currency ?? Text("UnknownCurrency"), cost.Labor, cost.Parts, cost.Vendor, cost.Total, cost.UnknownLabor, cost.UnknownParts }.Select(CsvCell)));
            return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        }
        public static string PacketSection(IEnumerable<ReadinessWorkOrderEvidence> entries)
        {
            string H(object value) => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.CurrentCulture));
            var html = new StringBuilder("<h2>" + H(Text("WorkOrders")) + "</h2><p>" + H(Text("WorkOrderPacketMethod")) + "</p><table><tr>");
            foreach (var key in new[] { "Number", "Title", "Status", "Priority", "Unit", "Asset", "DueOn", "Revision", "SafetyHolds" }) html.Append("<th>").Append(H(Text(key))).Append("</th>");
            html.Append("</tr>");
            foreach (var item in entries)
            {
                html.Append("<tr>");
                foreach (var value in new object[] { item.WorkOrderId, item.Title, Text("Status" + (WorkOrderStatus)item.Status), Text("Priority" + (WorkOrderPriority)item.Priority), item.UnitId, item.AssetId, item.DueOn?.ToString("u"), item.Revision, string.Join(", ", item.ActiveSafetyHoldIds) }) html.Append("<td>").Append(H(value)).Append("</td>");
                html.Append("</tr>");
            }
            return html.Append("</table>").ToString();
        }
    }
}

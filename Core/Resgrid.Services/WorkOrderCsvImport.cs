using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Resgrid.Model;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public static class WorkOrderCsvImport
    {
        // Stable interchange keys; presentation labels and examples are localized by the UI.
        public const string Header = "Title,Description,Type,Priority,UnitId,GroupId,AssetId,Location,Currency,EstimatedCost,DueUtc,CostCenter";
        public static WorkOrderBulkInput Parse(string text, string requestId)
        {
            if (text == null || Encoding.UTF8.GetByteCount(text) > 1024 * 1024) throw new WorkOrderException(400, "BulkCsvInvalid");
            using var reader = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            reader.SetDelimiters(","); string[] headers;
            try { headers = reader.ReadFields(); } catch (MalformedLineException) { throw new WorkOrderException(400, "BulkCsvInvalid"); }
            if (headers?.Length > 0) headers[0] = headers[0].TrimStart('\uFEFF');
            var keys = Header.Split(',');
            if (headers == null || !headers.SequenceEqual(keys, StringComparer.OrdinalIgnoreCase)) throw new WorkOrderException(400, "BulkCsvInvalid");
            var result = new WorkOrderBulkInput { RequestId = requestId }; var rowNumber = 1;
            while (!reader.EndOfData)
            {
                var row = new WorkOrderBulkRow { RowNumber = rowNumber++ }; result.Rows.Add(row);
                if (result.Rows.Count > 200) throw new WorkOrderException(400, "BulkInputInvalid");
                try
                {
                    var f = reader.ReadFields(); if (f?.Length != keys.Length) throw new FormatException();
                    int? Number(string v) => string.IsNullOrWhiteSpace(v) ? null : int.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture);
                    row.Import = new WorkOrderInput { Type = string.IsNullOrWhiteSpace(f[2]) ? WorkOrderType.Corrective : (WorkOrderType)int.Parse(f[2], CultureInfo.InvariantCulture), Priority = string.IsNullOrWhiteSpace(f[3]) ? WorkOrderPriority.Normal : (WorkOrderPriority)int.Parse(f[3], CultureInfo.InvariantCulture),
                        TargetUnitId = Number(f[4]), TargetGroupId = Number(f[5]), InventoryAssetId = string.IsNullOrWhiteSpace(f[6]) ? null : f[6],
                        DueOn = string.IsNullOrWhiteSpace(f[10]) ? null : DateTime.ParseExact(f[10], new[] { "yyyy-MM-ddTHH:mm:ss'Z'", "yyyy-MM-ddTHH:mm'Z'" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                        Content = new WorkOrderContent { Title = f[0], Description = f[1], LocationText = f[7], Currency = f[8], EstimatedCost = string.IsNullOrWhiteSpace(f[9]) ? null : decimal.Parse(f[9], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), CostCenter = f[11] } };
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or MalformedLineException) { row.Import = null; row.ParseError = "BulkCsvInvalid"; }
            }
            return result;
        }
    }
}

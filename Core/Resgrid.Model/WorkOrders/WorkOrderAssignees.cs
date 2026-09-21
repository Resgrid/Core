using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.WorkOrders
{
    /// <summary>Assignment routing metadata; legacy single assignments remain readable.</summary>
    public static class WorkOrderAssignees
    {
        public static List<string> Users(string json, string legacy) =>
            (json == null ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(json) ?? new())
            .Concat(string.IsNullOrEmpty(legacy) ? System.Array.Empty<string>() : new[] { legacy }).Distinct().ToList();

        public static List<int> Roles(string json, int? legacy) =>
            (json == null ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(json) ?? new())
            .Concat(legacy.HasValue ? new[] { legacy.Value } : System.Array.Empty<int>()).Distinct().ToList();
    }
}

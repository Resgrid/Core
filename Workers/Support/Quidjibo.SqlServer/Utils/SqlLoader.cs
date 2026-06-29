using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Quidjibo.SqlServer.Utils
{
    public class SqlLoader
    {
        private static readonly ConcurrentDictionary<string, string> Scripts = new ConcurrentDictionary<string, string>();

        public static async Task<string> GetScript(string scriptName)
        {
            if (Scripts.TryGetValue(scriptName, out var script))
            {
                return script;
            }

            var assembly = typeof(SqlLoader).GetTypeInfo().Assembly;
            var fullName = $"Quidjibo.SqlServer.Scripts.{scriptName}.sql";
            using (var stream = assembly.GetManifestResourceStream(fullName) ?? new MemoryStream())
            using (var reader = new StreamReader(stream))
            {
                script = await reader.ReadToEndAsync();
                Scripts.TryAdd(scriptName, script);
            }

            return script;
        }
    }
}
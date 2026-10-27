using System;
using System.Globalization;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Scriban.Runtime;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// Turns the flat dotted variables of an Incident Back Office trigger ("incident.period.number") into the nested Scriban objects a
	/// template reads ({{ incident.period.number }}). Only catalogued variables are placed (plan §13.7), so the context, the variable
	/// catalog and the sample data agree by construction.
	/// </summary>
	public static class IncidentWorkflowContext
	{
		public static void Apply(ScriptObject target, WorkflowTriggerEventType trigger, Func<string, string, object> value)
		{
			foreach (var (variable, type, _) in IncidentBackOfficeWorkflowPayload.For(trigger))
			{
				var parts = variable.Split('.');
				var node = target;
				for (var i = 0; i < parts.Length - 1; i++)
				{
					if (node[parts[i]] is not ScriptObject child)
						node[parts[i]] = child = new ScriptObject();
					node = child;
				}
				node[parts[parts.Length - 1]] = value(variable, type);
			}
		}

		/// <summary>Synthetic values for the designer's test render: obviously sample text, small numbers, a fixed date.</summary>
		public static object Sample(string variable, string type) => type switch
		{
			"int" => 2,
			"decimal" => 12.5m,
			"bool" => variable == "incident.is_exercise" ? (object)true : false,
			"datetime" => new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture),
			_ => variable switch
			{
				"incident.name" => "SAMPLE Ridge Fire (synthetic test data)",
				"incident.short_code" => "RIDGE",
				"incident.kind" => "Incident",
				_ => "SAMPLE " + variable
			}
		};
	}
}

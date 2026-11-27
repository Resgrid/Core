using System;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Scriban.Runtime;

namespace Resgrid.Services
{
	/// <summary>
	/// The template context and the sample data of the round-7C content triggers (<see cref="ContentWorkflowTriggers"/>): the payload's
	/// properties under the family's prefix, <c>change</c> from the run's trigger, and the member in the family's user-id property for the
	/// common user.* block. A payload the safe projection redacted keeps its shape (a redacted value reads as the redaction marker).
	/// </summary>
	public static class ContentWorkflowContext
	{
		/// <summary>Maps the payload into <paramref name="scriptObject"/>; returns the user id for the user.* block (null when none).</summary>
		public static string Apply(ScriptObject scriptObject, WorkflowTriggerEventType eventType, string eventPayloadJson)
		{
			var family = ContentWorkflowTriggers.For(eventType);
			if (family == null || scriptObject == null)
				return null;

			JObject payload;
			try
			{
				payload = string.IsNullOrWhiteSpace(eventPayloadJson) ? null : JObject.Parse(eventPayloadJson);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The {eventType} workflow payload could not be read.");
				payload = null;
			}

			if (payload == null)
				return null;

			var values = new ScriptObject();
			values["change"] = family.Changes[eventType];
			foreach (var variable in family.Variables)
				values[variable.Variable] = Value(payload[variable.Property], variable.DataType);
			scriptObject[family.Prefix] = values;

			if (family.UserIdProperty == null)
				return null;
			var userId = payload[family.UserIdProperty];
			return userId != null && userId.Type == JTokenType.String ? userId.Value<string>() : null;
		}

		/// <summary>The sample block of a round-7C trigger for template previews and test sends (synthetic values only).</summary>
		public static void AddSample(ScriptObject scriptObject, WorkflowTriggerEventType eventType)
		{
			var family = ContentWorkflowTriggers.For(eventType);
			if (family == null || scriptObject == null)
				return;

			var values = new ScriptObject();
			values["change"] = family.Changes[eventType];
			foreach (var variable in family.Variables)
				values[variable.Variable] = variable.Sample ?? (variable.DataType == "int" ? (object)0 : null);
			scriptObject[family.Prefix] = values;
		}

		/// <summary>An int the payload left empty reads 0 (as the other families' ids do); everything else as stored.</summary>
		private static object Value(JToken token, string dataType)
		{
			var value = WorkflowTemplateContextBuilder.ToScriptValue(token);
			if (value == null && dataType == "int")
				return 0;
			return value;
		}

		/// <summary>
		/// Publishes a workflow event after the change it describes is stored. Never fatal: a failure is logged and the change stands.
		/// </summary>
		public static void Publish<T>(IEventAggregator events, T workflowEvent) where T : class
		{
			if (events == null || workflowEvent == null)
				return;

			try
			{
				events.SendMessage(workflowEvent);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The {typeof(T).Name} workflow event could not be published.");
			}
		}
	}
}

using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Web.Mcp.ModelContextProtocol;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>
	/// Tool handlers return the Newtonsoft JObject payloads that ApiClient deserializes from the v4 API. The server
	/// must write them out as JSON, not as the nested empty arrays System.Text.Json produces for a JToken.
	/// </summary>
	[TestFixture]
	public sealed class McpServerToolResultTests
	{
		[Test]
		public async Task ToolsCall_ShouldSerializeJObjectPayloadAsJson()
		{
			// Arrange
			var apiResponse = JsonConvert.DeserializeObject<object>(@"{""Data"":[{""CallId"":""42"",""Name"":""Structure fire""}],""Status"":""success""}");
			var server = new McpServer("test", "1.0.0");
			server.AddTool("get_active_calls", "test tool", new System.Collections.Generic.Dictionary<string, object>(),
				_ => Task.FromResult<object>(new { success = true, data = apiResponse }));

			// Act
			var responseJson = await server.HandleRequestAsync(
				@"{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/call"",""params"":{""name"":""get_active_calls"",""arguments"":{}}}",
				CancellationToken.None);

			// Assert
			var text = JObject.Parse(responseJson)["result"]["content"][0].Value<string>("text");
			var toolResult = JObject.Parse(text);

			Assert.That(toolResult.Value<bool>("success"), Is.True);
			Assert.That(toolResult["data"]["Status"].Value<string>(), Is.EqualTo("success"));
			Assert.That(toolResult["data"]["Data"][0]["Name"].Value<string>(), Is.EqualTo("Structure fire"));
		}
	}
}

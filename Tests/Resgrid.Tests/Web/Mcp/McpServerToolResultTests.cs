using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Web.Mcp.ModelContextProtocol;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>
	/// How the server wraps a tool's return value in the MCP tools/call result: the payload as JSON text, and isError
	/// set when the tool reports a failure.
	/// </summary>
	[TestFixture]
	public sealed class McpServerToolResultTests
	{
		[Test]
		public async Task ToolsCall_ShouldSerializeJObjectPayloadAsJson()
		{
			// Arrange: tool handlers return the Newtonsoft JObject payloads that ApiClient deserializes from the v4 API,
			// which must come out as JSON, not the nested empty arrays System.Text.Json produces for a JToken.
			var apiResponse = JsonConvert.DeserializeObject<object>(@"{""Data"":[{""CallId"":""42"",""Name"":""Structure fire""}],""Status"":""success""}");

			// Act
			var result = await CallTool(_ => Task.FromResult<object>(new { success = true, data = apiResponse }));

			// Assert
			var toolResult = JObject.Parse(result["content"][0].Value<string>("text"));

			Assert.That(toolResult.Value<bool>("success"), Is.True);
			Assert.That(toolResult["data"]["Status"].Value<string>(), Is.EqualTo("success"));
			Assert.That(toolResult["data"]["Data"][0]["Name"].Value<string>(), Is.EqualTo("Structure fire"));
			Assert.That(result.Value<bool>("isError"), Is.False);
		}

		[Test]
		public async Task ToolsCall_ShouldSetIsErrorWhenTheToolReportsFailure()
		{
			var result = await CallTool(_ => Task.FromResult<object>(new { success = false, error = "Valid call ID is required" }));

			Assert.That(result.Value<bool>("isError"), Is.True);
			Assert.That(JObject.Parse(result["content"][0].Value<string>("text")).Value<string>("error"), Is.EqualTo("Valid call ID is required"));
		}

		[Test]
		public async Task ToolsCall_ShouldSetIsErrorForAnMcpToolError()
		{
			var result = await CallTool(_ => throw new McpToolErrorException(McpToolErrorException.AccessTokenExpired, "Refresh the token"));

			Assert.That(result.Value<bool>("isError"), Is.True);
			Assert.That(JObject.Parse(result["content"][0].Value<string>("text")).Value<string>("errorCode"), Is.EqualTo(McpToolErrorException.AccessTokenExpired));
		}

		[Test]
		public async Task ToolsCall_ShouldNotSetIsErrorWhenTheResultHasNoSuccessFlag()
		{
			var result = await CallTool(_ => Task.FromResult<object>(new JArray(new JObject { ["success"] = false })));

			Assert.That(result.Value<bool>("isError"), Is.False);
		}

		private static async Task<JObject> CallTool(Func<object, Task<object>> handler)
		{
			var server = new McpServer("test", "1.0.0");
			server.AddTool("test_tool", "test tool", new Dictionary<string, object>(), handler);

			var responseJson = await server.HandleRequestAsync(
				@"{""jsonrpc"":""2.0"",""id"":1,""method"":""tools/call"",""params"":{""name"":""test_tool"",""arguments"":{}}}",
				CancellationToken.None);

			return (JObject)JObject.Parse(responseJson)["result"];
		}
	}
}

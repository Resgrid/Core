using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NUnit.Framework;
using Resgrid.Web.Mcp;
using Resgrid.Web.Mcp.Infrastructure;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>
	/// The MCP server calls the v4 API over HTTP, so nothing but these tests connects its routes to the controllers.
	/// Every route in <see cref="V4Routes"/> must resolve to a v4 controller action that accepts the HTTP method the
	/// route is grouped under, and the MCP source must not call the API through any other string.
	/// </summary>
	[TestFixture]
	public sealed class McpRouteConformanceTests
	{
		private const string V4ControllerNamespace = "Resgrid.Web.Services.Controllers.v4";

		private static readonly Lazy<List<ApiRoute>> ApiRoutes = new Lazy<List<ApiRoute>>(BuildApiRouteTable);

		public static IEnumerable<TestCaseData> McpRoutes()
		{
			foreach (var methodGroup in typeof(V4Routes).GetNestedTypes())
			{
				foreach (var field in methodGroup.GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.IsLiteral))
				{
					yield return new TestCaseData(methodGroup.Name.ToUpperInvariant(), (string)field.GetRawConstantValue())
						.SetName($"V4Routes.{methodGroup.Name}.{field.Name}");
				}
			}

			yield return new TestCaseData("GET", ApiHealthProbe.HealthPath).SetName("ApiHealthProbe.HealthPath");
		}

		[TestCaseSource(nameof(McpRoutes))]
		public void McpRoute_ShouldResolveToV4ActionForItsHttpMethod(string httpMethod, string route)
		{
			Assert.That(route, Does.Not.Contain("?"), "V4Routes holds paths only; append the query string at the call site");

			var path = route.TrimStart('/');
			var matches = ApiRoutes.Value.Where(x => x.Pattern.IsMatch(path)).ToList();

			Assert.That(matches, Is.Not.Empty, $"No v4 controller action is routed at {route}");
			Assert.That(matches.Any(x => x.HttpMethods.Contains(httpMethod)), Is.True,
				$"{route} exists but does not accept {httpMethod}. It accepts: {string.Join(", ", matches.SelectMany(x => x.HttpMethods).Distinct())}");
		}

		[Test]
		public void McpSource_ShouldOnlyCallApiThroughV4Routes()
		{
			var root = RepositoryRoot();
			if (root == null)
				Assert.Ignore("Resgrid.sln not found above the test directory; the MCP source is not available to scan.");

			var mcpDirectory = Path.Combine(root, "Web", "Resgrid.Web.Mcp");
			var allowedFiles = new[]
			{
				Path.Combine(mcpDirectory, "V4Routes.cs"),
				Path.Combine(mcpDirectory, "Infrastructure", "ApiHealthProbe.cs") // HealthPath is covered by McpRoutes above
			};

			var literal = new Regex(@"""[^""\r\n]*api/v4/", RegexOptions.IgnoreCase);
			var offenders = Directory.EnumerateFiles(mcpDirectory, "*.cs", SearchOption.AllDirectories)
				.Where(x => !IsBuildOutput(x) && !allowedFiles.Contains(x))
				.SelectMany(file => File.ReadLines(file)
					.Select((line, index) => (line, index))
					.Where(x => !x.line.TrimStart().StartsWith("//") && literal.IsMatch(x.line))
					.Select(x => $"{Path.GetRelativePath(root, file)}:{x.index + 1}: {x.line.Trim()}"))
				.ToList();

			Assert.That(offenders, Is.Empty, "Add these routes to V4Routes so they are checked against the v4 controllers");
		}

		/// <summary>
		/// Builds the attribute-routed actions of every v4 controller, combining the controller and action templates
		/// the way ASP.NET Core does.
		/// </summary>
		private static List<ApiRoute> BuildApiRouteTable()
		{
			var routes = new List<ApiRoute>();

			var controllers = typeof(V4AuthenticatedApiControllerbase).Assembly.GetTypes()
				.Where(x => x.IsClass && !x.IsAbstract && typeof(ControllerBase).IsAssignableFrom(x)
					&& x.Namespace != null && x.Namespace.StartsWith(V4ControllerNamespace, StringComparison.Ordinal));

			foreach (var controller in controllers)
			{
				var controllerName = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
					? controller.Name.Substring(0, controller.Name.Length - "Controller".Length)
					: controller.Name;

				var prefixes = controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(x => x.Template).ToList();
				if (prefixes.Count == 0)
					prefixes.Add(string.Empty);

				foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
				{
					var httpAttributes = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();
					if (httpAttributes.Count == 0)
						continue;

					var methodRoutes = method.GetCustomAttributes<RouteAttribute>(inherit: true).Select(x => x.Template).ToList();

					foreach (var httpAttribute in httpAttributes)
					{
						// [HttpGet("x")] carries its own template; a bare [HttpGet] applies to the action's [Route] templates.
						var templates = httpAttribute.Template != null
							? new List<string> { httpAttribute.Template }
							: methodRoutes.DefaultIfEmpty(string.Empty).ToList();

						foreach (var prefix in prefixes)
						{
							foreach (var template in templates)
							{
								var combined = Combine(prefix, template)
									.Replace("[controller]", controllerName)
									.Replace("[action]", method.Name)
									.Replace("{VersionId:apiVersion}", "4");

								routes.Add(new ApiRoute(combined, ToPattern(combined), httpAttribute.HttpMethods.ToList()));
							}
						}
					}
				}
			}

			return routes;
		}

		private static string Combine(string prefix, string template)
		{
			if (template.StartsWith("/", StringComparison.Ordinal) || template.StartsWith("~/", StringComparison.Ordinal))
				return template.TrimStart('~', '/');

			if (string.IsNullOrEmpty(prefix))
				return template;

			if (string.IsNullOrEmpty(template))
				return prefix;

			return $"{prefix.TrimEnd('/')}/{template}";
		}

		/// <summary>Matches a route template case-insensitively, with each {parameter} segment matching any one segment.</summary>
		private static Regex ToPattern(string template)
		{
			var segments = template.Trim('/').Split('/')
				.Select(x => x.StartsWith("{", StringComparison.Ordinal) && x.EndsWith("}", StringComparison.Ordinal)
					? (x.StartsWith("{*", StringComparison.Ordinal) ? ".+" : "[^/]+")
					: Regex.Escape(x));

			return new Regex($"^{string.Join("/", segments)}$", RegexOptions.IgnoreCase);
		}

		private static bool IsBuildOutput(string path)
		{
			var separator = Path.DirectorySeparatorChar;
			return path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}bin{separator}");
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			return directory?.FullName;
		}

		private sealed class ApiRoute
		{
			public ApiRoute(string template, Regex pattern, List<string> httpMethods)
			{
				Template = template;
				Pattern = pattern;
				HttpMethods = httpMethods;
			}

			public string Template { get; }
			public Regex Pattern { get; }
			public List<string> HttpMethods { get; }
		}
	}
}

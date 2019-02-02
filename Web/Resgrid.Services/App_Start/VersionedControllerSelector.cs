
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Description;
using System.Web.Http.ModelBinding;
using System.Web.Http.Routing;
using System.Web.Http.ValueProviders;
using System.Web.Http.ValueProviders.Providers;

//	// https://github.com/Sebazzz/SDammann.WebApi.Versioning

internal static class HttpParameterBindingExtensions
{
	public static string Version(this string namespaceWithAVersion)
	{
		if (string.IsNullOrEmpty(namespaceWithAVersion))
			throw new ArgumentOutOfRangeException("namespaceWithAVersion");

		var version = "";
		var parts = namespaceWithAVersion.Split('.');
		foreach (var part in parts.Where(p => p.ToLower().StartsWith("version")))
		{
			int v;
			if (int.TryParse(part.ToLower().Replace("version", ""), out v))
				version = v.ToString();
		}

		return version;
	}
}

namespace SDammann.WebApi.Versioning
{
	public class VersionedApiExplorer : IApiExplorer
	{
		private readonly HttpConfiguration configuration;
		private Lazy<Collection<ApiDescription>> apiDescription;
		private const string ActionVariableName = "action";
		private const string ControllerVariableName = "controller";
		private static readonly Regex _actionVariableRegex = new Regex(String.Format(CultureInfo.CurrentCulture, "{{{0}}}", ActionVariableName), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex _controllerVariableRegex = new Regex(String.Format(CultureInfo.CurrentCulture, "{{{0}}}", ControllerVariableName), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		private ApiExplorer DefaultExplorer { get; set; }

		public VersionedApiExplorer(HttpConfiguration configuration)
		{
			this.configuration = configuration;
			this.apiDescription = new Lazy<Collection<ApiDescription>>(InitializeApiDescriptions);
			this.DefaultExplorer = new ApiExplorer(configuration);
		}

		public Collection<ApiDescription> ApiDescriptions
		{
			get { return this.apiDescription.Value; }
		}

		private Collection<ApiDescription> InitializeApiDescriptions()
		{
			Collection<ApiDescription> apiDescriptions = new Collection<ApiDescription>();
			var controllerSelector = configuration.Services.GetHttpControllerSelector();
			IDictionary<string, HttpControllerDescriptor> controllerMappings = controllerSelector.GetControllerMapping();
			if (controllerMappings != null)
			{
				foreach (var route in configuration.Routes)
					ExploreRouteControllers(controllerMappings, route, apiDescriptions);
			}
			return apiDescriptions;
		}

		private void ExploreRouteControllers(IDictionary<string, HttpControllerDescriptor> controllerMappings, IHttpRoute route, Collection<ApiDescription> apiDescriptions)
		{
			string routeTemplate = route.RouteTemplate;
			object controllerVariableValue;
			if (_controllerVariableRegex.IsMatch(routeTemplate))
			{
				// unbound controller variable, {controller}
				foreach (KeyValuePair<string, HttpControllerDescriptor> controllerMapping in controllerMappings)
				{
					controllerVariableValue = controllerMapping.Key;
					HttpControllerDescriptor controllerDescriptor = controllerMapping.Value;

					if (DefaultExplorer.ShouldExploreController(controllerVariableValue.ToString(), controllerDescriptor, route))
					{
						// expand {controller} variable
						string expandedRouteTemplate = _controllerVariableRegex.Replace(routeTemplate, controllerVariableValue.ToString());
						ExploreRouteActions(route, expandedRouteTemplate, controllerDescriptor, apiDescriptions);
					}
				}
			}
			else
			{
				// bound controller variable, {controller = "controllerName"}
				if (route.Defaults.TryGetValue(ControllerVariableName, out controllerVariableValue))
				{
					HttpControllerDescriptor controllerDescriptor;
					if (controllerMappings.TryGetValue(controllerVariableValue.ToString(), out controllerDescriptor) && DefaultExplorer.ShouldExploreController(controllerVariableValue.ToString(), controllerDescriptor, route))
					{
						ExploreRouteActions(route, routeTemplate, controllerDescriptor, apiDescriptions);
					}
				}
			}
		}

		private void ExploreRouteActions(IHttpRoute route, string localPath, HttpControllerDescriptor controllerDescriptor, Collection<ApiDescription> apiDescriptions)
		{
			ServicesContainer controllerServices = controllerDescriptor.Configuration.Services;
			ILookup<string, HttpActionDescriptor> actionMappings = controllerServices.GetActionSelector().GetActionMapping(controllerDescriptor);
			object actionVariableValue;
			if (actionMappings != null)
			{
				if (_actionVariableRegex.IsMatch(localPath))
				{
					// unbound action variable, {action}
					foreach (IGrouping<string, HttpActionDescriptor> actionMapping in actionMappings)
					{
						// expand {action} variable
						actionVariableValue = actionMapping.Key;
						string expandedLocalPath = _actionVariableRegex.Replace(localPath, actionVariableValue.ToString());
						PopulateActionDescriptions(actionMapping, actionVariableValue.ToString(), route, expandedLocalPath, apiDescriptions);
					}
				}
				else if (route.Defaults.TryGetValue(ActionVariableName, out actionVariableValue))
				{
					// bound action variable, { action = "actionName" }
					PopulateActionDescriptions(actionMappings[actionVariableValue.ToString()], actionVariableValue.ToString(), route, localPath, apiDescriptions);
				}
				else
				{
					// no {action} specified, e.g. {controller}/{id}
					foreach (IGrouping<string, HttpActionDescriptor> actionMapping in actionMappings)
					{
						PopulateActionDescriptions(actionMapping, null, route, localPath, apiDescriptions);
					}
				}
			}
		}

		private void PopulateActionDescriptions(IEnumerable<HttpActionDescriptor> actionDescriptors, string actionVariableValue, IHttpRoute route, string localPath, Collection<ApiDescription> apiDescriptions)
		{
			foreach (HttpActionDescriptor actionDescriptor in actionDescriptors)
			{
				if (DefaultExplorer.ShouldExploreAction(actionVariableValue, actionDescriptor, route))
				{
					PopulateActionDescriptions(actionDescriptor, route, localPath, apiDescriptions);
				}
			}
		}

		private void PopulateActionDescriptions(HttpActionDescriptor actionDescriptor, IHttpRoute route, string localPath, Collection<ApiDescription> apiDescriptions)
		{
			string apiDocumentation = GetApiDocumentation(actionDescriptor);

			// parameters
			IList<ApiParameterDescription> parameterDescriptions = CreateParameterDescriptions(actionDescriptor);

			// expand all parameter variables
			string finalPath;

			if (!TryExpandUriParameters(route, localPath, actionDescriptor, parameterDescriptions, out finalPath))
			{
				// the action cannot be reached due to parameter mismatch, e.g. routeTemplate = "/users/{name}" and GetUsers(id)
				return;
			}

			// request formatters
			ApiParameterDescription bodyParameter = parameterDescriptions.FirstOrDefault(description => description.Source == ApiParameterSource.FromBody);
			IEnumerable<MediaTypeFormatter> supportedRequestBodyFormatters = bodyParameter != null ?
				actionDescriptor.Configuration.Formatters.Where(f => f.CanReadType(bodyParameter.ParameterDescriptor.ParameterType)) :
				Enumerable.Empty<MediaTypeFormatter>();

			// response formatters
			Type returnType = actionDescriptor.ReturnType;
			IEnumerable<MediaTypeFormatter> supportedResponseFormatters = returnType != null ?
				actionDescriptor.Configuration.Formatters.Where(f => f.CanWriteType(returnType)) :
				Enumerable.Empty<MediaTypeFormatter>();

			// get HttpMethods supported by an action. Usually there is one HttpMethod per action but we allow multiple of them per action as well.
			IList<HttpMethod> supportedMethods = DefaultExplorer.GetHttpMethodsSupportedByAction(route, actionDescriptor);

			foreach (HttpMethod method in supportedMethods)
			{
				var description = new ApiDescription()
				{
					Documentation = apiDocumentation,
					HttpMethod = method,
					RelativePath = finalPath,
					ActionDescriptor = actionDescriptor,
					Route = route
				};

				description.SetResponseDescription(CreateResponseDescription(actionDescriptor));

				foreach (var mtf in supportedRequestBodyFormatters)
					description.SupportedRequestBodyFormatters.Add(mtf);
				foreach (var mtf in supportedResponseFormatters)
					description.SupportedResponseFormatters.Add(mtf);
				foreach (var par in parameterDescriptions)
					description.ParameterDescriptions.Add(par);

				apiDescriptions.Add(description);
			}
		}

		private ResponseDescription CreateResponseDescription(HttpActionDescriptor actionDescriptor)
		{
			Type type = (
				from attribute in actionDescriptor.GetCustomAttributes<ResponseTypeAttribute>()
				select attribute.ResponseType).FirstOrDefault<Type>();
			ResponseDescription responseDescription = new ResponseDescription()
			{
				DeclaredType = actionDescriptor.ReturnType,
				ResponseType = type,
				Documentation = this.GetApiResponseDocumentation(actionDescriptor)
			};
			return responseDescription;
		}

		private string GetApiResponseDocumentation(HttpActionDescriptor actionDescriptor)
		{
			var documentationProvider = DefaultExplorer.DocumentationProvider ?? actionDescriptor.Configuration.Services.GetDocumentationProvider();
			if (documentationProvider == null)
			{
				return null;
			}
			return documentationProvider.GetResponseDocumentation(actionDescriptor);
		}


		private IList<ApiParameterDescription> CreateParameterDescriptions(HttpActionDescriptor actionDescriptor)
		{
			IList<ApiParameterDescription> parameterDescriptions = new List<ApiParameterDescription>();
			HttpActionBinding actionBinding = GetActionBinding(actionDescriptor);

			// try get parameter binding information if available
			if (actionBinding != null)
			{
				HttpParameterBinding[] parameterBindings = actionBinding.ParameterBindings;
				if (parameterBindings != null)
				{
					foreach (HttpParameterBinding parameter in parameterBindings)
					{
						parameterDescriptions.Add(CreateParameterDescriptionFromBinding(parameter));
					}
				}
			}
			else
			{
				Collection<HttpParameterDescriptor> parameters = actionDescriptor.GetParameters();
				if (parameters != null)
				{
					foreach (HttpParameterDescriptor parameter in parameters)
					{
						parameterDescriptions.Add(CreateParameterDescriptionFromDescriptor(parameter));
					}
				}
			}

			return parameterDescriptions;
		}

		private ApiParameterDescription CreateParameterDescriptionFromDescriptor(HttpParameterDescriptor parameter)
		{
			ApiParameterDescription parameterDescription = new ApiParameterDescription();
			parameterDescription.ParameterDescriptor = parameter;
			parameterDescription.Name = parameter.Prefix ?? parameter.ParameterName;
			parameterDescription.Documentation = GetApiParameterDocumentation(parameter);
			parameterDescription.Source = ApiParameterSource.Unknown;
			return parameterDescription;
		}

		private ApiParameterDescription CreateParameterDescriptionFromBinding(HttpParameterBinding parameterBinding)
		{
			ApiParameterDescription parameterDescription = CreateParameterDescriptionFromDescriptor(parameterBinding.Descriptor);
			if (parameterBinding.WillReadBody)
			{
				parameterDescription.Source = ApiParameterSource.FromBody;
			}
			else if (parameterBinding.WillReadUri())
			{
				parameterDescription.Source = ApiParameterSource.FromUri;
			}

			return parameterDescription;
		}

		private static bool TryExpandUriParameters(IHttpRoute route, string routeTemplate, HttpActionDescriptor actionDescriptor, ICollection<ApiParameterDescription> parameterDescriptions, out string expandedRouteTemplate)
		{
			var parameterValuesForRoute = GetParameterValuesForRoute(parameterDescriptions);

			var paramString = new StringBuilder();

			if (parameterValuesForRoute.Count > 0)
			{
				paramString.Append("?");

				foreach (var param in parameterValuesForRoute)
					paramString.AppendFormat("{2}{0}={1}", param.Key, param.Value, paramString.ToString().Length > 1 ? "&" : string.Empty);
			}

			expandedRouteTemplate = route.RouteTemplate.Replace("/{id}", string.Empty)
				.Replace("/{action}", actionDescriptor.ActionName.Equals(route.Defaults["action"]) ? "" : string.Format("/{0}", actionDescriptor.ActionName))
				.Replace("{version}", actionDescriptor.ControllerDescriptor.Version())
				.Replace("{controller}", actionDescriptor.ControllerDescriptor.ControllerName)
				+ paramString.ToString();
			return true;
		}

		private static Dictionary<string, object> GetParameterValuesForRoute(IEnumerable<ApiParameterDescription> parameterDescriptions)
		{
			var parameterValuesForRoute = new Dictionary<string, object>();

			foreach (var paramDescriptor in parameterDescriptions.Where(pd => pd.Source == ApiParameterSource.FromUri))
			{
				Type parameterType = paramDescriptor.ParameterDescriptor.ParameterType;
				if (parameterType.IsPrimitive || parameterType == typeof(string) || parameterType == typeof(Nullable<>))
				{
					parameterValuesForRoute.Add(paramDescriptor.Name, "{" + paramDescriptor.Name + "}");
				}
				else
				{
					GetModelParameterValues(parameterType, parameterValuesForRoute);
				}
			}
			return parameterValuesForRoute;
		}

		private static void GetModelParameterValues(Type parameterType, Dictionary<string, object> parameterValuesForRoute)
		{
			foreach (var property in parameterType.GetProperties())
			{
				var ignoreAttribute = property.GetCustomAttributes(typeof(IgnoreDataMemberAttribute), false).FirstOrDefault();
				if (ignoreAttribute != null)
				{
					continue;
				}

				var paramName = property.Name;

				var nameAttribute = property.GetCustomAttributes(typeof(DataMemberAttribute), true).OfType<DataMemberAttribute>().LastOrDefault();
				if (nameAttribute != null)
				{
					paramName = nameAttribute.Name ?? paramName;
				}

				parameterValuesForRoute.Add(paramName, string.Format("{{{0}}}", paramName));
			}
		}

		private string GetApiDocumentation(HttpActionDescriptor actionDescriptor)
		{
			IDocumentationProvider documentationProvider = DefaultExplorer.DocumentationProvider ?? actionDescriptor.Configuration.Services.GetDocumentationProvider();
			if (documentationProvider == null)
			{
				return "No documentation available.";
			}

			return documentationProvider.GetDocumentation(actionDescriptor);
		}

		private string GetApiParameterDocumentation(HttpParameterDescriptor parameterDescriptor)
		{
			IDocumentationProvider documentationProvider = DefaultExplorer.DocumentationProvider ?? parameterDescriptor.Configuration.Services.GetDocumentationProvider();
			if (documentationProvider == null)
			{
				return "No documentation available.";
			}

			return documentationProvider.GetDocumentation(parameterDescriptor);
		}

		private static HttpActionBinding GetActionBinding(HttpActionDescriptor actionDescriptor)
		{
			HttpControllerDescriptor controllerDescriptor = actionDescriptor.ControllerDescriptor;
			if (controllerDescriptor == null)
			{
				return null;
			}

			ServicesContainer controllerServices = controllerDescriptor.Configuration.Services;
			IActionValueBinder actionValueBinder = controllerServices.GetActionValueBinder();
			HttpActionBinding actionBinding = actionValueBinder != null ? actionValueBinder.GetBinding(actionDescriptor) : null;
			return actionBinding;
		}

		Collection<ApiDescription> IApiExplorer.ApiDescriptions
		{
			get { return new Collection<ApiDescription>(this.ApiDescriptions.Select(a => a as ApiDescription).ToList()); }
		}
	}

	internal static class HttpParameterBindingExtensions
	{
		public static bool WillReadUri(this HttpParameterBinding parameterBinding)
		{
			if (parameterBinding == null)
				return false;

			IValueProviderParameterBinding valueProviderParameterBinding = parameterBinding as IValueProviderParameterBinding;
			if (valueProviderParameterBinding != null)
			{
				IEnumerable<ValueProviderFactory> valueProviderFactories = valueProviderParameterBinding.ValueProviderFactories;
				if (valueProviderFactories.Any() && valueProviderFactories.All(factory => factory is QueryStringValueProviderFactory || factory is RouteDataValueProviderFactory))
				{
					return true;
				}
			}

			return false;
		}

		public static string Version(this HttpControllerDescriptor controllerDescriptor)
		{
			string version = "???";
			if (controllerDescriptor != null)
			{
				var parts = controllerDescriptor.ControllerType.Namespace.Split('.');

				foreach (var part in parts.Where(HttpParameterBindingExtensions.IsVersionNumber))
				{
					return part.Substring("Version".Length).Replace("_", ".");
				}
			}
			return version;
		}

		internal static bool IsVersionNumber(string namespacePart)
		{
			return Regex.IsMatch(namespacePart, String.Format(@"{0}[0-9]+(_[0-9]+)*", "Version"));
		}
	}


	internal static class ApiDescriptionExtensions
	{
		public static void SetResponseDescription(this ApiDescription apiDescription, ResponseDescription responseDescription)
		{
			var property = GetPropertyFrom(apiDescription);
			SetPropertyTo(apiDescription, responseDescription, property);
		}

		private static System.Reflection.PropertyInfo GetPropertyFrom(ApiDescription apiDescription)
		{
			if (apiDescription == null)
				throw new ArgumentNullException("apiDescription");
			return apiDescription.GetType().GetProperty("ResponseDescription");
		}

		private static void SetPropertyTo(ApiDescription apiDescription, ResponseDescription responseDescription, System.Reflection.PropertyInfo property)
		{
			if (property != null)
				property.SetValue(apiDescription, responseDescription);
		}
	}
}

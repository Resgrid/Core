using System;
using System.Web.Http;
using System.Web.Http.Cors;

namespace Resgrid.Web.Services.App_Start
{
	public static class WebApiConfig
	{
		public static void Register(HttpConfiguration config)
		{
			var cors = new EnableCorsAttribute("*", "*", "*");
			config.EnableCors(cors);

			// enable API versioning
			config.Services.Replace(typeof(System.Web.Http.Dispatcher.IHttpControllerSelector), new SDammann.WebApi.Versioning.RouteVersionedControllerSelector(config));
			config.Services.Replace(typeof(System.Web.Http.Description.IApiExplorer), new SDammann.WebApi.Versioning.VersionedApiExplorer(config));

			//config.MessageHandlers.Add(new Resgrid.Web.Services.Middleware.OptionsHttpMessageHandler());

			var authTokenMessageHandler = new AuthTokenMessageHandler();
			authTokenMessageHandler.InnerHandler = new System.Web.Http.Dispatcher.HttpControllerDispatcher(config);

			//config.Routes.MapHttpRoute(
			//		name: "DefaultV3AuthApi",
			//		routeTemplate: "api/v3/auth/{action}",
			//		defaults: new { version = 3, controller = "AuthController" }
			//);

			//config.Routes.MapHttpRoute(
			//		name: "DefaultV3Api",
			//		routeTemplate: "api/v3/{controller}/{id}",
			//		defaults: new { version = 3, id = RouteParameter.Optional },
			//		constraints: null,
			//		handler: v3AuthTokenMessageHandler
			//);

			config.Routes.MapHttpRoute(
				name: "Redirect",
				routeTemplate: "r/{id}",
				defaults: new { controller = "Redirect" }
			);

			config.Routes.MapHttpRoute(
				name: "ConnectV3",
				routeTemplate: "api/v3/connect/{action}/{id}",
				defaults: new { controller = "Connect" }
			);

			config.Routes.MapHttpRoute(
					name: "DefaultApi",
					routeTemplate: "api/v{version}/{controller}/{action}/{id}",
					defaults: new { id = RouteParameter.Optional },
					constraints: null,
					handler: authTokenMessageHandler
			);

			config.Routes.MapHttpRoute(
					name: "DefaultServicesV1",
					routeTemplate: "services/v1/{action}",
					defaults: new { controller = "ServicesV1" }
			);

			config.Routes.MapHttpRoute(
					name: "FeedsService",
					routeTemplate: "feeds/{action}/{key}",
					defaults: new { controller = "Feeds" }
			);

			config.Routes.MapHttpRoute(
							name: "TextMessages",
							routeTemplate: "texts/{action}",
							defaults: new { controller = "TextMessages" }
			);

			config.Routes.MapHttpRoute(
					name: "TwilioProvider",
					routeTemplate: "twilio/{action}",
					defaults: new { controller = "TwilioProvider" }
			);

			config.Routes.MapHttpRoute(
					name: "TwilioProviderAction",
					routeTemplate: "twilio/VoiceCallAction/{userId}/{callId}",
					defaults: new { controller = "TwilioProvider", action = "VoiceCallAction", userId = Guid.Empty, callId = 0}
			);

			config.Routes.MapHttpRoute(
					name: "Email",
					routeTemplate: "email/{action}",
					defaults: new { controller = "Email" }
			);

			config.Routes.MapHttpRoute(
					name: "RegistrationService",
					routeTemplate: "DepartmentRegistration/{action}",
					defaults: new { controller = "DepartmentRegistration" }
			);

			config.Routes.MapHttpRoute(
					name: "SignalWireProvider",
					routeTemplate: "signalwire/{action}",
					defaults: new { controller = "SignalWire" }
			);

			// Configure SignalR with our JSON Serialization settings
			//var serializer = new Microsoft.AspNet.SignalR.Json.JsonNetSerializer(JsonSerializationExtensions.JsonSerializerSettings);
			//Microsoft.AspNet.SignalR.GlobalHost.DependencyResolver.Register(typeof(Microsoft.AspNet.SignalR.Json.IJsonSerializer), () => serializer);
		}
	}
}

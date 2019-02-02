using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Resgrid.Web.Services.App_Start;
using Resgrid.Web.Services.Controllers.Version2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Formatting;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;

namespace Resgrid.Web.Services.Controllers
{
	[JsonNetFormatterConfigAttributeForHelp]
	public abstract class HelpControllerBase : ApiController
	{
		public abstract ICollection<ApiDescription> GetApiSnapshot();

		protected ICollection<ApiDescription> GetFilteredVersions(string @namespace)
		{
			var expectedVersion = @namespace.Version();
			return (from VersionedApiDescription value in Configuration.Services.GetApiExplorer().ApiDescriptions
					where value.ActionDescriptor.ControllerDescriptor.ControllerType.Namespace == @namespace
					select (ApiDescription)value).ToCollection();
		}
	}


	public class JsonNetFormatterConfigAttributeForHelpAttribute : Attribute, System.Web.Http.Controllers.IControllerConfiguration
	{
		public void Initialize(System.Web.Http.Controllers.HttpControllerSettings settings,
			System.Web.Http.Controllers.HttpControllerDescriptor descriptor)
		{
			// Remove the JSON formatter.
			var jsonFormatter = settings.Formatters.JsonFormatter;
			settings.Formatters.Remove(jsonFormatter);

			// Add a custom json media-type formatter.
			var formatter = new JsonNetFormatter();
			settings.Formatters.Insert(0, formatter);
		}
	}

	public class JsonNetFormatter : MediaTypeFormatter
	{
		JsonSerializerSettings settings = new JsonSerializerSettings()
		{
			NullValueHandling = NullValueHandling.Include,
			ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
			PreserveReferencesHandling = PreserveReferencesHandling.All,
			Formatting = Formatting.Indented,
			Converters = new JsonConverter[]
			{
				new IsoDateTimeConverter(),
				new StringEnumConverter()
			},
		};

		public JsonNetFormatter()
		{
			SupportedMediaTypes.Add(new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
		}

		public override bool CanReadType(Type type)
		{
			return true;
		}

		public override bool CanWriteType(Type type)
		{
			return true;
		}

		public override Task<object> ReadFromStreamAsync(Type type, Stream readStream, System.Net.Http.HttpContent content, IFormatterLogger formatterLogger)
		{
			var task = Task<object>.Factory.StartNew(() =>
			{
				return readStream.DeserializeStream(type);
			});

			return task;
		}

		public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, System.Net.Http.HttpContent content, System.Net.TransportContext transportContext)
		{
			var task = Task.Factory.StartNew(() =>
			{
				string json = value.SerializeJson(settings);

				byte[] buf = System.Text.Encoding.Default.GetBytes(json);
				writeStream.Write(buf, 0, buf.Length);
				writeStream.Flush();
			});

			return task;
		}
	}

}
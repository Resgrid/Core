using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;
using System.Net.Http.Formatting;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.Version2
{
    public class JsonNetFormatterConfigAttribute : Attribute, System.Web.Http.Controllers.IControllerConfiguration
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
                string json = value.SerializeJson();

                byte[] buf = System.Text.Encoding.Default.GetBytes(json);
                writeStream.Write(buf, 0, buf.Length);
                writeStream.Flush();
            });

            return task;
        }
    }
}
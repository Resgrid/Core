using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

namespace Resgrid.Web.Services.Middleware
{

    public class OptionsHttpMessageHandler : System.Net.Http.DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            if (request.Method == System.Net.Http.HttpMethod.Options)
            {
                var apiExplorer = GlobalConfiguration.Configuration.Services.GetApiExplorer();

                var controllerRequested = request.GetRouteData().Values["controller"] as string;

                var supportedMethods = apiExplorer.ApiDescriptions
                    .Where(d =>
                    {
                        var controller = d.ActionDescriptor.ControllerDescriptor.ControllerName;
                        return string.Equals(
                            controller, controllerRequested, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(d => d.HttpMethod.Method)
                    .Distinct();

								if (supportedMethods != null && supportedMethods.Any() || (controllerRequested ?? "").Equals("ServicesV1", StringComparison.InvariantCultureIgnoreCase))
                {
                    return Task.Factory.StartNew(() =>
                    {
                        var resp = new HttpResponseMessage(HttpStatusCode.OK);
                        resp.Headers.Add("Access-Control-Allow-Methods", string.Join(",", supportedMethods.Concat(new[] { "ServicesV1", "ValidateSettings" })));
                        resp.Headers.Add("Access-Control-Allow-Headers", "Origin, X-Requested-With, Content-Type, Accept");

                        return resp;
                    });
                }
                else
                {
                    return Task.Factory.StartNew(() => request.CreateResponse(HttpStatusCode.NotFound));
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
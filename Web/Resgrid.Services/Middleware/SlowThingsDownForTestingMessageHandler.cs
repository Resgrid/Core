using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Resgrid.Web.Services.Middleware
{
    public class SlowThingsDownForTestingMessageHandler : System.Net.Http.DelegatingHandler
    {
        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(1000);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
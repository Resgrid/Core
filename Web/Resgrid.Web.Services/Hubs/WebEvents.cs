using Resgrid.Model.Events;
using Resgrid.Web.Services.ApplicationCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Hubs
{
    //public class WebEvents : PersistentConnection
    //{
    //    protected override Task OnConnected(IRequest request, string connectionId)
    //    {
    //        if (request.User.Identity.IsAuthenticated)
    //        {
    //            this.Groups.Add(connectionId, request.User.AuthToken().DepartmentId.ToString());
    //        }

    //        return base.OnConnected(request, connectionId);
    //    }

    //    protected override Task OnReconnected(IRequest request, string connectionId)
    //    {
    //        if (request.User.Identity.IsAuthenticated)
    //        {
    //            this.Groups.Add(connectionId, request.User.AuthToken().DepartmentId.ToString());
    //        }

    //        return base.OnReconnected(request, connectionId);
    //    }

    //    protected override Task OnReceived(IRequest request, string connectionId, string data)
    //    {
    //        return base.OnReceived(request, connectionId, data);
    //    }

    //    // not sure why this is necessary, but groups doesn't work without this function...
    //    protected IEnumerable<string> OnRejoiningGroups(IRequest request, IEnumerable<string> groups, string connectionId)
    //    {
    //        return groups;
    //    }
    //}
}

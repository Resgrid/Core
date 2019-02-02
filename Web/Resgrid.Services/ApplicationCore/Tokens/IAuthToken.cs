using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Web;
using Resgrid.Web.Services.App_Start;

namespace Resgrid.Web.Services.ApplicationCore.Tokens
{
    public interface IAuthToken
    {
        string UserName { get; }
        int DepartmentId { get; }
        string DepartmentCode { get; }
        bool IsTokenOfType(string tokenString);
        IAuthToken Decode(string authHeader);
        AuthenticationHeaderValue GetAuthHeaderValue(AuthToken authToken);
    }
}
using System.Net.Http.Headers;

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

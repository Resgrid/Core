using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Http;
using Resgrid.Web.Services.App_Start;

namespace Resgrid.Web.Services.ApplicationCore.Tokens
{
    public class UserAuthToken : IAuthToken
    {
        public string UserName { get; private set; }
        public int DepartmentId { get; private set; }
        public string DepartmentCode { get; private set; }

        public UserAuthToken(string userName, int departmentId, string departmentCode)
		{
			this.UserName = userName;
			this.DepartmentId = departmentId;
			this.DepartmentCode = departmentCode;
		}

        public bool IsTokenOfType(string tokenString)
        {
            try
            {
                var authBytes = Convert.FromBase64String(tokenString);
                var authStr = Encoding.ASCII.GetString(authBytes);

                if (authStr.StartsWith("S:"))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decodes the base64 encoded auth token
        /// 
        /// EX: "VXNlck5hbWV8MXxBQkNE" turns into "UserName|1|ABCD" which is a pipe delimeted list of "UserName|DepartmentId|DepartmentCode" and returns an AuthToken object with this metadata parsed out.
        /// </summary>
        /// <param name="authHeader"></param>
        /// <returns></returns>
        public IAuthToken Decode(string authHeader)
        {
            if (string.IsNullOrEmpty(authHeader))
                throw new ArgumentException("value cannot be null or empty", "authHeader");

            string[] rows = null;

            try
            {
                var authBytes = Convert.FromBase64String(authHeader);
                var authStr = Encoding.ASCII.GetString(authBytes);
                rows = authStr.Split('|');
            }
            catch (Exception ex)
            {
                //TODO: log exception here? with metada used in authHeader?
                throw new HttpResponseException(HttpStatusCode.Unauthorized);
            }

            if (rows.Length != 3)
                throw new HttpResponseException(HttpStatusCode.Unauthorized);

            string username = rows[0];
            int departmentId;
            string departmentCode = rows[2];

            if (string.IsNullOrEmpty(username))
                throw new HttpResponseException(HttpStatusCode.Unauthorized);

            if (!int.TryParse(rows[1], out departmentId))
                throw new HttpResponseException(HttpStatusCode.Unauthorized);

            if (string.IsNullOrEmpty(departmentCode))
                throw new HttpResponseException(HttpStatusCode.Unauthorized);


            return new UserAuthToken(username, departmentId, departmentCode);
        }

        public string Encode(List<string> data)
        {
            if (data == null || data.Count != 3)
                throw new ArgumentException("value cannot be null, empty or contain anything other then 3 elements", "data");

            var buffer = Encoding.ASCII.GetBytes(string.Join("|", new[] { data[0], data[1], data[2] }));
            var authHeader = Convert.ToBase64String(buffer);
            return authHeader;
        }

        public AuthenticationHeaderValue GetAuthHeaderValue(AuthToken authToken)
        {
            var authString = Encode(new List<string> {authToken.UserName, authToken.DepartmentId.ToString(), authToken.DepartmentCode});
            return new AuthenticationHeaderValue("Basic", authString);
        }
    }
}
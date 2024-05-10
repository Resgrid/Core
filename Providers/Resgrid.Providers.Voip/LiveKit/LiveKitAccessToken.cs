using System;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Principal;
using System.Linq;
using Resgrid.Config;

namespace Resgrid.Providers.Voip.LiveKit
{
	public class LiveKitAccessToken
	{
		private LiveKitGrant grants;
		private string identity;
		private double minutesForClaimTtl;

		/// <summary>
		/// AccessToken
		/// </summary>
		/// <param name="key">API key</param>
		/// <param name="secret">API secret</param>
		/// <param name="grants">Grants to include as claims</param>
		public LiveKitAccessToken(LiveKitGrant grants, double minutesForClaimTtl = 5, string identity = null)
		{
			this.grants = grants;
			this.identity = identity;
			this.minutesForClaimTtl = minutesForClaimTtl;
		}

		/// <summary>
		/// Generates a new JWT token with 1 day validity
		/// </summary>
		/// <returns>A JWT token string</returns>
		public string GetToken(string room = null)
		{
			if (room != null)
			{
				grants.video.room = room;
			}

			var tokenDescriptor = new SecurityTokenDescriptor
			{
				Claims = grants.ToDictionary(),
				Expires = DateTime.UtcNow.AddMinutes(minutesForClaimTtl),
				Issuer = VoipConfig.LiveKitServerApiKey,
				SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(VoipConfig.LiveKitServerApiSecret)), SecurityAlgorithms.HmacSha256Signature)
			};
			var tokenHandler = new JwtSecurityTokenHandler();
			var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);

			if (this.identity != null)
			{
				token.Payload.Add("sub", this.identity);
			}

			return tokenHandler.WriteToken(token);
		}

		/// <summary>
		/// Verify a given signed token with issuer and lifetime validation
		/// </summary>
		/// <param name="token">A signed token</param>
		/// <returns>SHA256 Claim included in the token</returns>
		public string VerifyToken(string token)
		{
			var tokenHandler = new JwtSecurityTokenHandler();
			var parameters = new TokenValidationParameters()
			{
				ValidateLifetime = true,
				ValidateAudience = false,
				ValidateIssuer = true,
				ValidIssuer = VoipConfig.LiveKitServerApiKey,
				IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(VoipConfig.LiveKitServerApiSecret))
			};
			SecurityToken validated;
			IPrincipal principal = tokenHandler.ValidateToken(token, parameters, out validated);

			return ((JwtSecurityToken)validated).Claims.First(x => x.Type == "sha256").Value;
		}
	}
}

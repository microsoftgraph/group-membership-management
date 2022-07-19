using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace WebAppWithAuth.Authentication
{
    public class CustomUserFactory : AccountClaimsPrincipalFactory<RemoteUserAccount>
    {
        public CustomUserFactory(IAccessTokenProviderAccessor accessor) : base(accessor) { }

        public async override ValueTask<ClaimsPrincipal> CreateUserAsync(
            RemoteUserAccount account,
            RemoteAuthenticationUserOptions options)
        {
            var user = await base.CreateUserAsync(account, options);

            if (user.Identity.IsAuthenticated)
            {
                var identity = (ClaimsIdentity)user.Identity;

                var groupClaims = identity.Claims.Where(x => x.Type == "groups").ToArray();
                var allClaims = identity.Claims.Where(x => x.Type.Contains("group")).ToList();

                if (groupClaims.Any())
                {
                    foreach (var existingClaim in groupClaims)
                    {
                        identity.RemoveClaim(existingClaim);
                    }


                    List<Claim> claims = new List<Claim>();

                    foreach (var g in groupClaims)
                    {
                        var groupGuids = JsonSerializer.Deserialize<string[]>(g.Value);

                        foreach (var claim in groupGuids)
                        {
                            claims.Add(new Claim(groupClaims.First().Type, claim));
                        }

                    }

                    foreach (var claim in claims)
                    {
                        identity.AddClaim(claim);
                    }
                }

                return user;
            }
            else
            {
                return null;
            }
        }
    }
}
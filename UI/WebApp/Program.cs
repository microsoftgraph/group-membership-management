// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

using WebAppWithAuth;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using WebAppWithAuth.Authentication;
using Microsoft.Graph;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

String[] scopes = new String[2];

scopes[0] = "https://graph.microsoft.com/User.Read";
scopes[1] = "https://graph.microsoft.com/Directory.Read.All";
builder.Services.AddMicrosoftGraphClient(scopes);

builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    options.ProviderOptions.DefaultAccessTokenScopes.Add("https://graph.microsoft.com/User.Read");
    options.ProviderOptions.DefaultAccessTokenScopes.Add("https://graph.microsoft.com/Directory.Read.All");
});

builder.Services.AddScoped<IAuthorizationHandler, GroupRequirementHandler>(services =>
{
    var gmmAdminsGroupId = services.GetRequiredService<IConfiguration>().GetSection("AppSettings").GetValue<string>("GmmAdminsGroupId");

    return new GroupRequirementHandler(services.GetRequiredService<GraphServiceClient>(), gmmAdminsGroupId); ;
});

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("MemberOfGmmAdminGroup",
        policy => policy.Requirements.Add(new GroupRequirement()));
});

await builder.Build().RunAsync();

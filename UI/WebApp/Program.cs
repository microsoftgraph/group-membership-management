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
using Microsoft.Graph.ExternalConnectors;
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

//builder.Services.AddSingleton<IAuthorizationHandler>((services) =>
//{
//    return new GroupRequirementHandler(services.GetRequiredService<GraphServiceClient>());
//});
builder.Services.AddScoped<IAuthorizationHandler, GroupRequirementHandler>(services =>
{
    return new GroupRequirementHandler(services.GetRequiredService<GraphServiceClient>());
});
//builder.Services.AddApiAuthorization().AddAccountClaimsPrincipalFactory<CustomUserFactory>();
builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("MemberOfGmmAdminGroup",   // TODO: hardcoded policy name should be moved to appsettings or smthg!
        policy => policy.Requirements.Add(new GroupRequirement("6b4a9b13-6f63-4658-af10-9160a32f806c")));  // TODO: move to appsettings.json
});

//builder.Services.AddScoped<GraphServiceClient>((services) =>
//{
//    return new GraphServiceClient(services);
//});

await builder.Build().RunAsync();

# WebAPI Setup

WebAPI is used by GMM UI and other GMM Durable Functions to get information about the groups that are managed by GMM as well as their respective job definition.

## Create the WebAPI application and populate prereqs keyvault

The following PowerShell script will create a new application, `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` and will also save these settings in the prereqs keyvault:

-   webApiClientId
-   webApiTenantId
-   webApiClientSecret
-   webApiCertificateName

Note that this script will create an new application and authentication will be done using a client id and client secret pair. If you prefer to use a certificate you need to provide the name of your certificate which must be present on your prereqs keyvault.

From your `PowerShell 7.x` command prompt navigate to the `Scripts\ApplicationSetupScripts\` folder of your `Public` repo and run these commands:

    1.    . ./Set-WebApiAzureADApplication.ps1
    2.    Set-WebApiAzureADApplication	-SubscriptionName "<subscription-name>" `
                                        -SolutionAbbreviation "<solution-abbreviation>" `
                                        -EnvironmentAbbreviation "<environment-abbreviation>" `
                                        -TenantId "<tenant-id>" `
                                        -Clean $false `
                                        -Verbose
Follow the instructions on the screen.

Note:
TenantId <tenant-id> - This is the tenant where your GMM resources are located, i.e. keyvaults, storage account.
DevTenantId <dev-tenant-id> - If the application is going to be installed in a different tenant, set that tenant id here. This is an optional parameter, if not provided TenantId value is used by default.

## Roles as policy to gate functionality

In order to control access to the WebAPI, several roles are created when the WebAPI application is created by the script above.

The roles are:

- Job Owner Reader
    - Users with this role have **read** access to Membership Management page.
    - They can view onboarded destinations that they own.

- Job Owner Enabler
    - Users with this role can use the UI to enable or disable destinations that they own.

- Job Owner Deleter
    - Users with this role can use the UI to delete destinations that they own.

- Job Owner Configuration Editor
    - Users with this role can edit the configuration of destinations that they own.

- Job Owner Writer
    - Users with this role have **read write** access to groups that they own in the Membership Management page.
    - They can view onboarded destinations that they own.
    - They can submit updates or onboarding requests for destinations that they own.
    - They can delete destinations they own.

- Job Tenant Reader
    - Users with this role have access to Membership Management page.
    - Users with this role can **read** all destinations in the tenant, whether they own the destinations being synced or not.

- Job Tenant Writer
    - Users with this role have access to Membership Management page.
    - Users with this role can **update** all destinations in the tenant, whether they own the destinations being synced or not.
    - They can submit onboarding requests for groups, whether they own them or not.

- Submission Reviewer
    - View Submission Requests for all groups​.
    - Approve/decline Submission requests​.
    - View user information from custom source.
    - _Note: for Submission Reviewers to be able to see all pending requests, they need to also have the Job Tenant Reader role_

- Submission Rejector
    - View Submission Requests for all groups​.
    - Decline Submission requests​.
    - View user information from custom source.
    - _Note: for Submission Rejectors to be able to see all pending requests, they need to also have the Job Tenant Reader role_

- Hyperlink Administrator
    - Users with this role can **add, update, and remove** custom urls from the Admin Center page.

- Custom Membership Provider Administrator
    - Users with this role can **add, update, and remove** custom field names from the Admin Center page.

- General Settings Administrator
    - Users with this role can **update** general settings from the Admin Center page.

- Reset Administrator
    - Users with this role can **reset, stop** GMM from the Admin Center page.

- AI Sync Job Viewer
    - Users with this role can view **AI-generated sync explanations** for all destinations in the tenant.
    - Group owners can always see AI explanations for their own groups without this role.

## Add a role to a group

Login and follow these steps in the tenant that was set in "AppTenantId" to run the Set-WebApiAzureADApplication.ps1 script in the previous step.

1. From the Azure Portal locate and open "Microsoft Entra ID"
2. On the left menu select "Enterprise Applications"
3. Search for the webapi application, the name follows this convention `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` i.e. gmm-webapi-int.
You might need to change the "Application Type" filter to "All Applications".
4. Click on the name of your application.
5. On the left menu select "Users and groups".
6. Click on "Add user/group"
7. Under "Users and groups", click on "None selected" and search for the group created in the previous step and select it.
8. Under "Select a role" click on "None selected", from the roles list select the appropriate role for this group (or individual user).
9. Click "Assign"

Note: Individual users can be added and granted the proper permission, if you decide not to use a group.

## Add trusted client applications

The WebAPI will be called by the GMM UI. In order to allow it to call the WebAPI, it needs to be added as trusted client application.

Create UI application by following [UI\Documentation\UISetup.md](../../../../../UI/Documentation/UISetup.md).

1. From the Azure Portal locate and open "Microsoft Entra ID"
2. On the left menu select "App Registrations"
3. Search for the webapi application, the name follows this convention `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` i.e. gmm-webapi-int.
4. Click on the name of your application.
5. On the left menu select "Expose an API".
6. Click on "Add a client application"
7. Provide the Application/Client ID of your GMM UI application, the name follows this convention `<solutionAbbreviation>`-ui-`<environmentAbbreviation>` i.e. gmm-ui-int.
Make sure to select the Authorized scope.
8. Click "Add application"

## Add the WebAPI application to the GMM UI as API Permission
1. From the Azure Portal locate and open "Microsoft Entra ID"
2. On the left menu select "App Registrations"
3. Search for the ui application, the name follows this convention `<solutionAbbreviation>`-ui-`<environmentAbbreviation>` i.e. gmm-ui-int.
4. Click on the name of your application.
5. On the left menu select "API Permissions".
6. Click on "Add a permission"
7. Click on "APIs my organization uses" and type the WebAPI Application ID
8. Select the WebAPI application and click on "Delegated permissions"
9. Check "user_impersonation" on and click "Add permissions"
10. Click "Grant admin consent for <tenant-name>"

* To properly setup the WebAPI you will need to configure the parameters in the `WebApi/Infrastructure/compute/parameters` for your environment.
If you have a custom domain, follow the instructions [here](#setting-up-a-custom-domain). If not, skip on to the instructions [here](#using-the-default-domain).

## Setting up a custom domain
If you have a custom domain ('contoso.com', for example) and want to use it, you will need to upgrade your App Service Plan. You can set the API custom domain in the `apiHostname` parameter as `api.contoso.com`.
This way, your parameter file will look like this
```
    {
        "$schema": "https://schema.management.azure.com/schemas/2015-01-01/deploymentParameters.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {
            "apiHostname": {
                "value": "api.contoso.com"
            }
        }
    }
```
Then, once the deployment finishes, follow these steps:

1. Go to your App Service resource and select `Custom domains`.
1. There, click on `+ Add custom domain` and enter your custom domain details. You can choose to include your own TLS/SSL certificate, or use an App Service Managed Certificate.
1. Once you have completed and validated the custom domain. Click `Add`.

Finally, you will need to update your App registration to include this custom domain. To do so, make sure you:
1. Login and follow these steps in the tenant where the application was created.
1. From the Azure Portal locate and open "Microsoft Entra ID"
1. On the left menu, select "App registrations"
1. Search for the webapi application, the name follows this convention `<solutionAbbreviation>`-webapi-`<environmentAbbreviation>` i.e. gmm-webapi-int.
1. Click on the name of your application.
1. On the left menu, select "Expose an API"
1. In the Application ID URI, set your custom domain here. i.e. `api://api.contoso.com`.

## Using the default domain
If you do not wish to set up a custom domain, you can leverage the one included in the F1 service plan, remove the `apiHostname` parameter file to use the default `<solutionAbbreviation>-compute-<solutionAbbreviation>-webapi.azurewebsites.net`

## CORS configuration

The WebAPI's allowed origins are configured differently for **production-like environments** (INT/UA/Prod) versus **local development**. There is no hardcoded CORS allowlist in `Program.cs` for non-Development environments.

### Production-like environments — App Service CORS blade

For any deployed environment, the WebAPI's allowed origins live in the App Service's CORS blade — surfaced in the Azure Portal at **App Service → API → CORS**, and represented in ARM as `siteConfig.cors.allowedOrigins` on the WebAPI's `Microsoft.Web/sites` resource.

Origins are populated **automatically as part of the deploy pipeline** by the `Set-ConfigureWebApiCors` PowerShell function in `Deployment/Set-ConfigureWebApiCors.ps1`. The function:

1. Queries the deployed UI Static Web App for its default hostname (`<solutionAbbreviation>-ui` by default, or a custom `StaticWebAppName` argument).
2. Adds the UI's custom domain hostname too, if one is configured.
3. Writes the resulting `https://` origins to the WebAPI's `siteConfig.cors.allowedOrigins`, preserving any pre-existing entries.
4. Mirrors the same origins onto the SignalR service's `AllowedOrigin` list (used for the `/servicestatus` hub).

No bicep parameter or per-env parameter file change is needed when a new origin must be added — the function re-discovers the UI hostnames every deploy. To add a one-off origin out-of-band (e.g. to allow a temporary preview deployment to call the API), edit the App Service's CORS blade in the Portal directly; the next pipeline run will preserve your manual addition.

ASP.NET Core CORS middleware (`app.UseCors(...)`) is **not** registered in non-Development environments. The App Service CORS blade is the only CORS gate. This is deliberate — Microsoft's guidance is explicit:

> "Don't try to use App Service CORS and your own CORS code together. If you try to use them together, App Service CORS takes precedence and your own CORS code has no effect."
> — `learn.microsoft.com/en-us/azure/app-service/app-service-web-tutorial-rest-api`

`supportCredentials: true` is set in `Infrastructure/compute/appService.bicep` and persists across deploys.

### Local development — `appsettings.Development.json`

For local `dotnet run`, the React UI on `http://localhost:3000` is allowed via an `IsDevelopment()`-gated `app.UseCors(...)` block in `Program.cs` that reads its origin list from configuration:

```json
// appsettings.Development.json
{
  "Cors": {
    "AllowedOrigins": [ "http://localhost:3000" ]
  }
}
```

To add a second local origin (e.g. `http://localhost:5173` for a Vite dev server), append it to that array. Do **not** add origins here that should be available outside local development — `appsettings.Development.json` is loaded only when `ASPNETCORE_ENVIRONMENT=Development`.

## Debugging the deployed WebAPI

The recommended workflow for investigating issues on a deployed WebAPI is **Application Insights** — not flipping `ASPNETCORE_ENVIRONMENT=Development` on the App Service.

### Why not flip `ASPNETCORE_ENVIRONMENT=Development` on prod?

Setting that env var on a deployed App Service unlocks **every** `if (app.Environment.IsDevelopment())` block in `Program.cs` in lockstep — currently four distinct concerns:

| What gets activated | Effect |
|---|---|
| `IdentityModelEventSource.ShowPII = true` | PII is written into request traces and forwarded to telemetry |
| `app.UseDeveloperExceptionPage()` | Stack traces are returned in HTTP response bodies — visible to whoever called the API |
| `app.UseSwaggerUI()` + `app.UseSwagger()` | The entire API surface becomes browsable at `/swagger` |
| `app.UseCors(...)` reading `appsettings.Development.json` | `http://localhost:3000` is allowed to make credentialed calls against the deployed API |

### Workflow 1 — Exception investigation

Application Insights captures every unhandled exception that bubbles through the ASP.NET Core pipeline (via `AddApplicationInsightsTelemetry()` at `Program.cs`), with the full parsed stack trace, the inbound request, and any correlated outbound dependencies. The `ApplicationInsights:ConnectionString` is wired in by `Infrastructure/compute/template.bicep` for every environment.

To investigate an exception:

1. **Azure Portal** → App Insights resource for the environment (`<solutionAbbreviation>-data-<environmentAbbreviation>`) → **Failures** blade. Exceptions are grouped by `problemId`; click into one for the full stack trace and correlated request.
2. **Logs** blade, paste the following KQL:
   ```kusto
   exceptions
   | where timestamp > ago(1h)
   | where cloud_RoleName has "webapi"
   | order by timestamp desc
   | project timestamp, type, outerMessage, operation_Name, problemId, details
   ```
3. **From the command line:**
   ```powershell
   az monitor app-insights query `
     --app <solutionAbbreviation>-data-<environmentAbbreviation> `
     --resource-group <solutionAbbreviation>-data-<environmentAbbreviation> `
     --analytics-query 'exceptions | where cloud_RoleName has "webapi" | order by timestamp desc | take 10' `
     --offset 1d
   ```
4. **For live repro / "watch a fix get deployed":** App Insights → **Live Metrics** — full sampled stack traces appear within ~1 second, no cost.

### Workflow 2 — Endpoint testing

When you want to call deployed endpoints to verify behaviour, use an out-of-process HTTP client with a bearer token, not Swagger UI:

```powershell
# Get a bearer token scoped to the WebAPI app registration
$token = az account get-access-token `
  --resource api://<solutionAbbreviation>-webapi-<environmentAbbreviation> `
  --query accessToken -o tsv

# Call any endpoint
curl https://<solutionAbbreviation>-compute-<environmentAbbreviation>-webapi.azurewebsites.net/api/v1/jobs `
  -H "Authorization: Bearer $token"
```

Equivalent flows in **Bruno** (file-based, can be checked into the repo as `.bru` files), **Postman**, **Insomnia**, or **VS Code REST Client** (`.http` files) are all preferred over enabling Swagger UI on a deployed App Service. The OpenAPI spec is available locally — run the WebAPI on your dev box and point your client at `http://localhost:<port>/swagger/v1/swagger.json` — so you don't lose the API discoverability that Swagger UI normally provides.

### Workflow 3 — Verbose logging (no env change)

If you need richer log output from `ILogger.LogInformation` / `LogDebug` calls that are normally filtered out of telemetry, raise the App Insights log-level filter via an App Setting (no redeploy required):

```powershell
az webapp config appsettings set `
  --name <solutionAbbreviation>-compute-<environmentAbbreviation>-webapi `
  --resource-group <solutionAbbreviation>-compute-<environmentAbbreviation> `
  --settings Logging__ApplicationInsights__LogLevel__Default=Debug
```

Revert to `Warning` (the default in `appsettings.json`) when you're done. App Service restarts automatically when settings change.

`az webapp log tail` is also available for streaming stdout/stderr including unhandled exception messages in real-time:

```powershell
az webapp log tail `
  --name <solutionAbbreviation>-compute-<environmentAbbreviation>-webapi `
  --resource-group <solutionAbbreviation>-compute-<environmentAbbreviation>
```
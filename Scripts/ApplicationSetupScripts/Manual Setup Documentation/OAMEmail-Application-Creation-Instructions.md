# Manual OAM Email Azure AD Application Creation Instructions

This document provides step-by-step instructions for manually creating the OAM (Outlook Actionable Messages) Email Azure AD application that is typically created by the `Set-OAMEmailApplication.ps1` script.

## Overview

The OAM Email application serves as the authentication provider for GMM's Outlook Actionable Messages integration. It enables Microsoft's Actionable Messages service to authenticate and authorize requests to GMM's email notification system.

## Prerequisites

- **Azure AD Global Administrator** or **Application Administrator** permissions
- Access to the Azure Portal (https://portal.azure.com)
- Knowledge of your:
  - Solution abbreviation (e.g., `gmm`)
  - Environment abbreviation (e.g., `prod`, `int`, `dev`)
  - OAM Provider ID (obtained from the Actionable Email Developer Dashboard)

## Step-by-Step Instructions

### 1. Create the Application Registration

1. Navigate to **Azure Portal** > **Microsoft Entra ID** > **App registrations**
2. Click **New registration**
3. Configure the basic settings:
   - **Name**: `{SolutionAbbreviation}-OAM-{EnvironmentAbbreviation}`
     - Example: `gmm-OAM-prod`
   - **Supported account types**: **Accounts in this organizational directory only (Single tenant)**
   - **Redirect URI**: Leave blank
4. Click **Register**
5. **Note the Application (client) ID** - you will need this for the next steps

### 2. Create Service Principal

The service principal is required for the application to function:

1. In your **App registration**, go to the **Overview** tab
2. Click on **Managed application in local directory** link
   - This will automatically create the service principal
3. Alternatively, verify it exists in **Microsoft Entra ID** > **Enterprise applications**
4. Search for your application name: `{SolutionAbbreviation}-OAM-{EnvironmentAbbreviation}`

### 3. Set Application ID URI (Expose an API)

This is the critical configuration for Outlook Actionable Messages:

1. Go to **Expose an API** tab
2. Click **Add** next to **Application ID URI**
3. **Replace** the default value with the Actionable Messages format:
   ```
   api://auth-am-{OamProviderId}/{ApplicationId}
   ```
   - Example: `api://auth-am-94992322-3b35-4d97-8ca9-d7e82040fe29/df95190e-d16e-489d-9d19-e8389f7435ec`
   - **OamProviderId**: Your Outlook Actionable Messages provider ID
   - **ApplicationId**: The Application (client) ID from step 1
4. Click **Save**

### 4. Add a Scope

1. In the **Expose an API** tab, click **Add a scope**
2. Configure the scope:
   - **Scope name**: `Global` (or your preferred scope name)
   - **Who can consent?**: **Admins only**
   - **Admin consent display name**: `Global`
   - **Admin consent description**: `Global scope for Outlook Actionable Messages`
   - **User consent display name**: `Global`
   - **User consent description**: `Global scope for Outlook Actionable Messages`
   - **State**: **Enabled**
3. Click **Add scope**
4. **Note the Scope ID** (GUID) - you will need this for the next step

### 5. Add Pre-authorized Client Application

This authorizes Microsoft's Actionable Messages service to access your application:

1. In the **Expose an API** tab, under **Authorized client applications**, click **Add a client application**
2. Configure:
   - **Client ID**: `48af08dc-f6d2-435f-b2a7-069abd99c086`
     - This is Microsoft's Actionable Messages client application ID
   - **Authorized scopes**: ✅ Check the scope you created (e.g., `Global`)
3. Click **Add application**

### 6. Verification Checklist

Verify your application has the following configuration:

#### Basic Properties
- [ ] **Display Name**: `{SolutionAbbreviation}-OAM-{EnvironmentAbbreviation}`
- [ ] **Service Principal**: Created

#### API Configuration (Expose an API)
- [ ] **Application ID URI**: `api://auth-am-{OamProviderId}/{ApplicationId}`
- [ ] **Scope**: Created with name `Global` (or custom name)
  - [ ] Type: Admin consent only
  - [ ] State: Enabled
- [ ] **Pre-authorized Application**: 
  - [ ] Client ID: `48af08dc-f6d2-435f-b2a7-069abd99c086`
  - [ ] Authorized for your scope

#### What is NOT Required
- ❌ No redirect URIs needed
- ❌ No client secrets needed
- ❌ No API permissions (Microsoft Graph, etc.) needed
- ❌ No public client flows needed

## Configuration Summary

| Setting | Value |
|---------|-------|
| Display Name | `{SolutionAbbreviation}-OAM-{EnvironmentAbbreviation}` |
| Application ID URI | `api://auth-am-{OamProviderId}/{ApplicationId}` |
| Scope Name | `Global` (default) |
| Scope Type | Admin consent only |
| Pre-authorized Client | `48af08dc-f6d2-435f-b2a7-069abd99c086` |

## Common Issues and Troubleshooting

### Issue: "Invalid Application ID URI format"
- **Solution**: Ensure the URI follows the exact format: `api://auth-am-{OamProviderId}/{ApplicationId}`
- **Check**: Both the OamProviderId and ApplicationId are valid GUIDs

### Issue: "Insufficient privileges to complete the operation"
- **Solution**: Ensure you have Application Administrator or Global Administrator permissions
- **Check**: Your account has sufficient privileges in Azure AD

### Issue: "Pre-authorized application cannot be added"
- **Solution**: Ensure you have created the scope first before adding the pre-authorized application
- **Check**: The scope exists and is enabled in the "Expose an API" section

### Issue: "Scope not found when adding pre-authorized client"
- **Solution**: Refresh the page after creating the scope, then try adding the pre-authorized client again
- **Check**: The scope shows up in the "Scopes defined by this application" section

### Issue: "Actionable Messages not authenticating"
- **Solution**: Verify the Application ID URI matches exactly what's registered in the Actionable Email Developer Dashboard
- **Check**: The OamProviderId in the URI matches your registered provider

### Issue: "Service principal not found"
- **Solution**: Click on "Managed application in local directory" in the app registration Overview
- **Check**: Search in Enterprise applications for your app registration

## Related Resources

- [Outlook Actionable Messages Documentation](https://docs.microsoft.com/en-us/outlook/actionable-messages/)
- [Actionable Email Developer Dashboard](https://outlook.office.com/connectors/oam/publish)
- [Register your service with the actionable email developer dashboard](https://docs.microsoft.com/en-us/outlook/actionable-messages/email-dev-dashboard)

---

**Note**: These instructions are based on the configuration created by `Set-OAMEmailApplication.ps1`. For automated setup, use the PowerShell script instead of manual configuration.
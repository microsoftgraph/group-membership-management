# Manual FunctionAuth Azure AD Application Creation Instructions

This document provides step-by-step instructions for manually creating the FunctionAuth Azure AD application that is typically created by the `Set-FunctionAuthApplication.ps1` script.

## Overview

The FunctionAuth application is the shared authentication audience used by GMM function apps and related callers. GMM uses this app registration to:

- Configure Function App authentication (`authsettingsV2`) with the FunctionAuth application client ID
- Validate allowed audiences (`api://{FunctionAuthAppClientId}`)
- Acquire tokens for protected function endpoints

## Important Tenant Behavior

FunctionAuth is different from other GMM app registrations:

- In a **single-tenant** deployment, create it in that tenant.
- In a **two-tenant** deployment (one tenant for Azure resources, another for the directory/users), create FunctionAuth in the **Azure resources/deployment tenant**.

Do **not** create FunctionAuth in the directory tenant when those tenants differ.

This guide is for **same-tenant setup** (the app registration tenant and deployment tenant are the same).

## Prerequisites

- **Azure AD Global Administrator** or **Application Administrator** permissions
- Access to the Azure Portal (https://portal.azure.com)
- Access to the GMM prereqs Key Vault:
  - `{SolutionAbbreviation}-prereqs-{EnvironmentAbbreviation}`
- Knowledge of your:
  - `SolutionAbbreviation`
  - `EnvironmentAbbreviation`

## Step-by-Step Instructions

### 1. Create the Application Registration

Create this app registration in the **same tenant where GMM Azure resources are deployed**.

1. Navigate to **Azure Portal** > **Microsoft Entra ID** > **App registrations**
2. Click **New registration**
3. Configure the basic settings:
   - **Name**: `{SolutionAbbreviation}-FunctionAuth-{EnvironmentAbbreviation}`
     - Example: `gmm-FunctionAuth-prod`
   - **Supported account types**: **Accounts in this organizational directory only (Single tenant)**
   - **Redirect URI**: Leave blank
4. Click **Register**

### 2. Configure Basic Application Properties

#### 2.1 Set Sign-in Audience
1. Go to **Authentication** tab
2. Under **Supported account types**, ensure **Accounts in this organizational directory only** is selected

#### 2.2 Ensure Public Client Flows Are Disabled
1. Go to **Authentication** tab
2. Scroll to **Advanced settings**
3. Set **Allow public client flows** to **No**

> Expected configuration: `signInAudience = AzureADMyOrg`, `isFallbackPublicClient = false`

### 3. Configure Expose an API

#### 3.1 Add OAuth Scope
1. Go to **Expose an API** tab
2. If prompted, click **Set** for Application ID URI only if required by the portal workflow
3. Click **Add a scope**
4. Configure the scope as follows:
   - **Scope name**: `client_impersonation`
   - **Who can consent?**: **Admins and users**
   - **Admin consent display name**: `FunctionAuth client impersonation`
   - **Admin consent description**: `FunctionAuth client impersonation`
   - **User consent display name**: `FunctionAuth client impersonation`
   - **User consent description**: `FunctionAuth client impersonation`
   - **State**: **Enabled**
5. Click **Add scope**

#### 3.2 Set Application ID URI
1. In **Expose an API**, set **Application ID URI** to:
   - `api://{FunctionAuthAppClientId}`
2. Click **Save**

> This URI must match the app’s own client ID.

### 4. Set Access Token Version

1. Open the **Manifest** tab
2. Ensure token version is set to v2:
   - `requestedAccessTokenVersion`: `2`
3. Click **Save**

### 5. Ensure Service Principal Exists

The service principal (enterprise application) is usually created automatically.

1. In the app registration **Overview**, click **Managed application in local directory**
2. Confirm the corresponding Enterprise Application exists:
   - Name: `{SolutionAbbreviation}-FunctionAuth-{EnvironmentAbbreviation}`

If not present, create it by opening the app registration from **Enterprise applications** and completing creation.

### 6. Continue Deployment Manual Flow

If you are running `Deploy-Resources.ps1` in manual app-registration mode:

1. Complete all required app registrations
2. Return to the deployment script prompt
3. Press **ENTER** to continue
4. Let the built-in validation run (`Test-FunctionAuthApplication`)

---

**Note**: These instructions are based on the configuration created by `Set-FunctionAuthApplication.ps1`. For automated setup, use the PowerShell script instead of manual configuration.
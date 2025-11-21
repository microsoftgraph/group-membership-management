# Manual UI Azure AD Application Creation Instructions

This document provides step-by-step instructions for manually creating the UI Azure AD application that is typically created by the `Set-UIAzureADApplication.ps1` script.

## Overview

The UI application serves as the frontend Single Page Application (SPA) for the Group Membership Management (GMM) system. It's a React-based web application that authenticates users and calls the GMM Web API.

## Prerequisites

- **Azure AD Global Administrator** or **Application Administrator** permissions
- Access to the Azure Portal (https://portal.azure.com)
- Knowledge of your solution abbreviation and environment abbreviation

## Step-by-Step Instructions

### 1. Create the Application Registration

1. Navigate to **Azure Portal** > **Microsoft Entra ID** > **App registrations**
2. Click **New registration**
3. Configure the basic settings:
   - **Name**: `{SolutionAbbreviation}-ui-{EnvironmentAbbreviation}`
     - Example: `gmm-ui-prod`
   - **Supported account types**: **Accounts in this organizational directory only (Single tenant)**
   - **Redirect URI**: Leave blank for now
4. Click **Register**

### 2. Configure Basic Application Properties

#### 2.1 Set Sign-in Audience
1. Go to **Authentication** tab
2. Under **Supported account types**, ensure **Accounts in this organizational directory only** is selected

#### 2.2 Set Public Client Setting
1. Go to **Authentication** tab
2. Scroll down to **Advanced settings**
3. Ensure **Allow public client flows** is set to **No**

### 3. Configure Single Page Application (SPA) Platform

#### 3.1 Add SPA Platform
1. Go to **Authentication** tab
2. Click **Add a platform**
3. Select **Single-page application**
4. Add the redirect URIs (you might have to input the first one, click configure, and then input the others):
   - `http://localhost:3000`
5. Click **Configure**

### 4. Configure Web Platform Settings (for Implicit Grant)

#### 4.1 Configure Implicit Grant Settings
1. In the **Authentication** tab, under **Implicit grant and hybrid flows**:
   - ✅ Check **Access tokens (used for implicit flows)**
   - ✅ Check **ID tokens (used for implicit and hybrid flows)**
2. Click **Save**

### 5. Set Application ID URI

1. Go to **Expose an API** tab
2. Click **Add** next to Application ID URI
3. Accept the default value: `api://{ApplicationId}`
4. Click **Save**

### 6. Configure API Permissions

1. Go to **API permissions** tab
2. Remove any default permissions if present
3. Click **Add a permission**
4. Select **Microsoft Graph**
5. Choose **Delegated permissions**
6. Search for and select the following permissions:
   - **User.Read** (`e1fe6dd8-ba31-4d61-89e7-88639da4683d`)
   - **User.ReadBasic.All** (`b340eb25-3456-403f-be2f-af7a0d370277`)
7. Click **Add permissions**
8. Click **Grant admin consent for [Your Organization]**
9. Click **Yes** to confirm

### 7. Create Application Secret (Optional - only if using client secret authentication)

1. Go to **Certificates & secrets** tab
2. Click **New client secret**
3. Configure:
   - **Description**: `UI Application Secret`
   - **Expires**: Choose appropriate expiration (e.g., 24 months)
4. Click **Add**
5. **Important**: Copy the secret value immediately (it won't be shown again)
6. Input the secret when the deployment script prompts you for it. 

### 8. Create Service Principal

#### Method 1: Azure Portal (Automatic)
The service principal is automatically created when you first access certain sections of your app registration:

1. In your **App registration**, go to the **Overview** tab
2. Click on **Managed application in local directory** link
   - This will automatically create the service principal and take you to the Enterprise Applications view
3. Alternatively, go to **Microsoft Entra ID** > **Enterprise applications**
4. Search for your application name: `{SolutionAbbreviation}-ui-{EnvironmentAbbreviation}`
   - Example: `gmm-ui-prod`
5. If it appears in the list, the service principal already exists

#### Method 2: Azure Portal (Manual Creation)
If the service principal doesn't exist automatically:

1. Go to **Microsoft Entra ID** > **Enterprise applications**
2. Click **New application**
3. Click **Create your own application**
4. Select **Register an application to integrate with Azure AD (App you're developing)**
   - Use the same name as the app registration: `{SolutionAbbreviation}-ui-{EnvironmentAbbreviation}`
5. Search for and select your existing app registration: `{SolutionAbbreviation}-ui-{EnvironmentAbbreviation}`
   - Example: `gmm-ui-prod`
6. This will create the corresponding service principalServicePrincipal -AppId "{YourApplicationId}"

### 10. Verification Checklist

Verify your application has the following configuration:

#### Basic Properties
- [ ] **Display Name**: `{SolutionAbbreviation}-ui-{EnvironmentAbbreviation}`
- [ ] **Sign-in Audience**: `AzureADMyOrg`
- [ ] **Public Client**: Disabled (`isFallbackPublicClient: false`)

#### Authentication Configuration
- [ ] **SPA Platform**: Configured with redirect URIs:
  - [ ] `http://localhost:3000`
  - [ ] Production URL (environment-specific)
- [ ] **Web Platform**: Configured (for implicit grant)
- [ ] **Implicit Grant**: Both access tokens and ID tokens enabled

#### API Configuration
- [ ] **Application ID URI**: `api://{ApplicationId}`

#### Permissions
- [ ] **Microsoft Graph Permissions**:
  - [ ] `User.Read` (delegated)
  - [ ] `User.ReadBasic.All` (delegated)
- [ ] **Admin Consent**: Granted for both permissions

#### Optional
- [ ] **Client Secret**: Created and securely stored (if needed)
- [ ] **Service Principal**: Created

## Common Issues and Troubleshooting

### Issue: "Invalid redirect URI"
- **Solution**: Ensure redirect URIs match exactly what's configured in the SPA platform settings
- **Check**: Protocol (http vs https), domain, port, and path

### Issue: "CORS errors in browser"
- **Solution**: Verify the app is configured as a Single Page Application, not a Web app
- **Check**: SPA platform is added with correct redirect URIs

### Issue: "Insufficient privileges"
- **Solution**: Ensure you have Application Administrator or Global Administrator permissions

### Issue: "Authentication fails in production"
- **Solution**: Verify the production redirect URI is correctly configured for your environment
- **Check**: Environment-specific URL format is correct

### Issue: "API calls fail with 401 Unauthorized"
- **Solution**: Ensure the UI app is pre-authorized in the Web API application
- **Check**: Web API app > Expose an API > Authorized client applications

### Issue: "User permissions not working"
- **Solution**: Verify admin consent was granted for all required permissions
- **Check**: API permissions tab shows green checkmarks


## Integration with GMM System

Once created, this UI application will:

1. **Authenticate GMM users** through Microsoft Entra ID
2. **Provide secure access** to the GMM web interface
3. **Call the GMM Web API** using access tokens
4. **Enable self-service** group membership management for group owners
5. **Display job status** and configuration options

---

**Note**: These instructions are based on the configuration created by `Set-UIAzureADApplication.ps1`. For automated setup, use the PowerShell script instead of manual configuration.
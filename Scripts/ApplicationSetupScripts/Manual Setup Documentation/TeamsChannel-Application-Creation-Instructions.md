# Manual Teams Channel Azure AD Application Creation Instructions

This document provides step-by-step instructions for manually creating the Teams Channel Azure AD application that is typically created by the `Set-TeamsChannelAzureADApplication.ps1` script.

## Overview

The Teams Channel application serves as the authentication provider for GMM's Teams Channel integration functionality. It enables the GMM system to read and update Microsoft Teams channel memberships through Microsoft Graph API using delegated permissions.

## Prerequisites

- **Azure AD Global Administrator** or **Application Administrator** permissions
- Access to the Azure Portal (https://portal.azure.com)
- Knowledge of your solution abbreviation and environment abbreviation
- Understanding of Microsoft Teams channel management requirements

## Step-by-Step Instructions

### 1. Create the Application Registration

1. Navigate to **Azure Portal** > **Microsoft Entra ID** > **App registrations**
2. Click **New registration**
3. Configure the basic settings:
   - **Name**: `{SolutionAbbreviation}-TeamsChannel-{EnvironmentAbbreviation}`
     - Example: `gmm-TeamsChannel-prod`
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
3. Set **Allow public client flows** to **Yes**

### 3. Configure Web Platform Settings

#### 3.1 Add Web Platform
1. Go to **Authentication** tab
2. Click **Add a platform**
3. Select **Web**
4. Add the redirect URIs (you might have to input the first one, click configure, and then input the others):
   - `https://{SolutionAbbreviation}-compute-{EnvironmentAbbreviation}-teamschannelupdater.azurewebsites.net`
   - `https://{SolutionAbbreviation}-compute-{EnvironmentAbbreviation}-teamschannelmembershipobtainer.azurewebsites.net`
   - `http://localhost` (for local development/testing)
   - Examples:
     - `https://gmm-compute-prod-teamschannelupdater.azurewebsites.net`
     - `https://gmm-compute-prod-teamschannelmembershipobtainer.azurewebsites.net`
     - `http://localhost`
5. Click **Save**

#### 3.2 Configure Implicit Grant Settings
1. In the **Authentication** tab, under **Implicit grant and hybrid flows**:
   - ✅ Check **Access tokens (used for implicit flows)**
   - ✅ Check **ID tokens (used for implicit and hybrid flows)**
2. Click **Save**

### 4. Set Application ID URI

1. Go to **Expose an API** tab
2. Click **Add** next to Application ID URI
3. Accept the default value: `api://{ApplicationId}`
4. Click **Save**

### 5. Configure API Permissions

#### 5.1 Add Microsoft Graph Permissions
1. Go to **API permissions** tab
2. Remove any default permissions if present
3. Click **Add a permission**
4. Select **Microsoft Graph**
5. Choose **Delegated permissions**
6. Search for and select the following permissions:
   - **ChannelMember.ReadWrite.All** - Read and write channel memberships
   - **Channel.ReadBasic.All** - Read basic channel information
7. Click **Add permissions**

#### 5.2 Grant Admin Consent
1. Click **Grant admin consent for [Your Organization]**
2. Click **Yes** to confirm
3. Verify both permissions show green checkmarks with "Granted for [Your Organization]"

### 6. Create Application Secret (Optional - only if using client secret authentication)

1. Go to **Certificates & secrets** tab
2. Click **New client secret**
3. Configure:
   - **Description**: `GMM Teams Channel Secret`
   - **Expires**: Choose appropriate expiration (recommended: 24 months)
4. Click **Add**
5. **Important**: Copy and store the secret value immediately (it won't be shown again)
6. Input the secret when the deployment script prompts you for it. 

### 7. Create Service Principal

#### Method 1: Azure Portal (Automatic)
The service principal is automatically created when you first access certain sections of your app registration:

1. In your **App registration**, go to the **Overview** tab
2. Click on **Managed application in local directory** link
   - This will automatically create the service principal and take you to the Enterprise Applications view
3. Alternatively, go to **Microsoft Entra ID** > **Enterprise applications**
4. Search for your application name: `{SolutionAbbreviation}-TeamsChannel-{EnvironmentAbbreviation}`
   - Example: `gmm-TeamsChannel-prod`
5. If it appears in the list, the service principal already exists

#### Method 2: Azure Portal (Manual Creation)
If the service principal doesn't exist automatically:

1. Go to **Microsoft Entra ID** > **Enterprise applications**
2. Click **New application**
3. Click **Create your own application**
4. Select **Register an application to integrate with Azure AD (App you're developing)**
   - Use the same name as the app registration: `{SolutionAbbreviation}-TeamsChannel-{EnvironmentAbbreviation}`
5. Search for and select your existing app registration: `{SolutionAbbreviation}-TeamsChannel-{EnvironmentAbbreviation}`
   - Example: `gmm-TeamsChannel-prod`
6. This will create the corresponding service principalServicePrincipal -AppId "{YourApplicationId}"

### 8. Verification Checklist

Verify your application has the following configuration:

#### Basic Properties
- [ ] **Display Name**: `{SolutionAbbreviation}-TeamsChannel-{EnvironmentAbbreviation}`
- [ ] **Sign-in Audience**: `AzureADMyOrg`
- [ ] **Public Client**: Enabled (`isFallbackPublicClient: true`)

#### Authentication Configuration
- [ ] **Web Platform**: Configured with redirect URIs:
  - [ ] `https://{solution}-compute-{env}-teamschannelupdater.azurewebsites.net`
  - [ ] `https://{solution}-compute-{env}-teamschannelmembershipobtainer.azurewebsites.net`
  - [ ] `http://localhost`
- [ ] **Implicit Grant**: Both access tokens and ID tokens enabled

#### API Configuration
- [ ] **Application ID URI**: `api://{ApplicationId}`

#### Permissions
- [ ] **Microsoft Graph Delegated Permissions**:
  - [ ] `ChannelMember.ReadWrite.All` with admin consent
  - [ ] `Channel.ReadBasic.All` with admin consent
- [ ] **Admin Consent**: Granted for both permissions

#### Security
- [ ] **Client Secret**: Created and securely stored
- [ ] **Service Principal**: Created

## Common Issues and Troubleshooting

### Issue: "Insufficient privileges to complete the operation"
- **Solution**: Ensure you have Application Administrator or Global Administrator permissions
- **Check**: Your account has sufficient privileges in Azure AD

### Issue: "Permission not granted"
- **Solution**: Verify admin consent was granted for both Microsoft Graph permissions
- **Check**: API permissions tab shows green checkmarks for both permissions

### Issue: "Authentication fails in Teams Channel operations"
- **Solution**: Verify the application is configured as a public client
- **Check**: "Allow public client flows" is set to "Yes"

### Issue: "Redirect URI mismatch"
- **Solution**: Ensure redirect URIs match exactly what's configured
- **Check**: Verify environment abbreviation and solution abbreviation are correct

### Issue: "Application secret expired"
- **Solution**: Create a new client secret and update GMM configuration
- **Check**: Monitor secret expiration dates and renew before expiry

### Issue: "Service principal not found"
- **Solution**: Verify the service principal was created successfully
- **Check**: Search in Enterprise applications for your app registration

---

**Note**: These instructions are based on the configuration created by `Set-TeamsChannelAzureADApplication.ps1`. For automated setup, use the PowerShell script instead of manual configuration.
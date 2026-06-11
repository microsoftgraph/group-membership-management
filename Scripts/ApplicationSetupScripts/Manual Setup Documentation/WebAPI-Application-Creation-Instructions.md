# Manual Web API Azure AD Application Creation Instructions

This document provides step-by-step instructions for manually creating the Web API Azure AD application that is typically created by the `Set-WebApiAzureADApplication.ps1` script.

## Overview

The Web API application serves as the backend API for the Group Membership Management (GMM) system. It requires specific configurations for authentication, authorization, and integration with the UI application.

## Prerequisites

- **Azure AD Global Administrator** or **Application Administrator** permissions
- Access to the Azure Portal (https://portal.azure.com)
- Knowledge of your solution abbreviation and environment abbreviation
- UI Application ID (IMPORTANT: the UI application must be created first.)

## Step-by-Step Instructions

### 1. Create the Application Registration

1. Navigate to **Azure Portal** > **Microsoft Entra ID** > **App registrations**
2. Click **New registration**
3. Configure the basic settings:
   - **Name**: `{SolutionAbbreviation}-webapi-{EnvironmentAbbreviation}`
     - Example: `gmm-webapi-prod`
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

### 3. Configure Web Platform Settings

#### 3.1 Add Web Platform
1. Go to **Authentication** tab
2. Click **Add a platform**
3. Select **Web**
4. Add the redirect URI:
   - `https://{SolutionAbbreviation}-compute-{EnvironmentAbbreviation}-webapi.azurewebsites.net/swagger/oauth2-redirect.html`
   - Example: `https://gmm-compute-prod-webapi.azurewebsites.net/swagger/oauth2-redirect.html`
5. Click **Configure**

#### 3.2 Configure Implicit Grant Settings
1. In the **Authentication** tab, under **Implicit grant and hybrid flows**:
   - ✅ Check **Access tokens (used for implicit flows)**
   - ✅ Check **ID tokens (used for implicit and hybrid flows)**
2. Click **Save**

### 4. Configure API Settings

#### 4.1 Set Application ID URI
1. Go to **Expose an API** tab
2. Click **Add** next to Application ID URI
3. Accept the default value: `api://{ApplicationId}`
4. Click **Save**

#### 4.2 Add OAuth2 Permission Scope
1. In **Expose an API** tab, click **Add a scope**
2. Configure the scope:
   - **Scope name**: `user_impersonation`
   - **Who can consent**: **Admins and users**
   - **Admin consent display name**: `WebAPI user impersonation`
   - **Admin consent description**: `WebAPI user impersonation`
   - **User consent display name**: `WebAPI user impersonation`
   - **User consent description**: `WebAPI user impersonation`
   - **State**: **Enabled**
3. Click **Add scope**

#### 4.3 Configure Access Token Version
1. Go to **Manifest** tab
2. Find the `requestedAccessTokenVersion` property
3. Change its value from `null` to `2`
4. Click **Save**

### 5. Configure API Permissions

1. Go to **API permissions** tab
2. Click **Add a permission**
3. Select **Microsoft Graph**
4. Choose **Delegated permissions**
5. Search for and select: **User.Read**
6. Click **Add permissions**
7. Click **Grant admin consent for [Your Organization]**
8. Click **Yes** to confirm

### 6. Configure Optional Claims

1. Go to **Token configuration** tab
2. Click **Add optional claim**
3. Select **Access** token type
4. Find and check **upn** (User Principal Name)
5. Click **Add**
6. If prompted about Microsoft Graph permissions, click **Turn on the Microsoft Graph User.Read permission**

### 7. Configure Application Roles

Application roles define the permissions that users and applications have when accessing the GMM Web API. These roles enable role-based access control (RBAC) for different GMM operations.

#### 7.1 Access App Roles Configuration
1. Go to **App roles** tab
2. Click **Create app role**

#### 7.2 Create Each Application Role

Create the following 13 application roles. For each role, click **Create app role** and configure:

**Role 1: Job Reader**
- **Display name**: `Job Reader`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Job.Read.OwnedBy`
- **Description**: `Can read owned destinations in the tenant.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 2: Job Owner Enabler/Disabler**
- **Display name**: `Job Owner Enabler/Disabler`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Job.Enable.OwnedBy`
- **Description**: `Can enable or disable owned destinations in the tenant.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 3: Job Owner Deleter**
- **Display name**: `Job Owner Deleter`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Job.Delete.OwnedBy`
- **Description**: `Can delete the job from GMM (disable GMM sync).`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 4: Job Owner Configuration Editor**
- **Display name**: `Job Owner Configuration Editor`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Job.EditConfiguration.OwnedBy`
- **Description**: `Can update owned destinations' configuration.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 5: Job Owner Writer**
- **Display name**: `Job Owner Writer`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Job.ReadWrite.OwnedBy`
- **Description**: `Can create, view, and update owned destinations in the tenant.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 6: Job Tenant Reader**
- **Display name**: `Job Tenant Reader`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Job.Read.All`
- **Description**: `Can read all destinations in the tenant.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 7: Job Tenant Writer**
- **Display name**: `Job Tenant Writer`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Job.ReadWrite.All`
- **Description**: `Can create, view, and update all destinations in the tenant.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 8: Submission Reviewer**
- **Display name**: `Submission Reviewer`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Submission.ReadWrite.All`
- **Description**: `Can view and manage Submission Requests for all groups.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 9: Submission Rejector**
- **Display name**: `Submission Rejector`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Submission.Reject.All`
- **Description**: `Can view and reject Submission Requests for all groups.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 10: Hyperlink Administrator**
- **Display name**: `Hyperlink Administrator`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Hyperlink.ReadWrite.All`
- **Description**: `Can add, update, or remove custom URLs.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 11: Custom Membership Provider Administrator**
- **Display name**: `Custom Membership Provider Administrator`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `CustomSource.ReadWrite.All`
- **Description**: `Can add, update, or remove custom field names.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 12: General Settings Administrator**
- **Display name**: `General Settings Administrator`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `GeneralSettings.ReadWrite.All`
- **Description**: `Can update general settings.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

**Role 13: Reset Administrator**
- **Display name**: `Reset Administrator`
- **Allowed member types**: ☑️ **Users/Groups** and ☑️ **Applications**
- **Value**: `Operations.Reset`
- **Description**: `Can reset or stop GMM.`
- **Do you want to enable this app role?**: ✅ **Yes**
- Click **Apply**

#### 7.3 Verify Application Roles

After creating all roles, verify:
1. Go to **App roles** tab
2. Confirm all 13 roles are listed and **Enabled**
3. Each role should show:
   - Correct **Display name**
   - Correct **Value**
   - **Allowed member types**: Both **Users/Groups** and **Applications**
   - **Status**: Enabled

> **Note**: These roles control access to different GMM operations. After deployment, you will assign users and groups to these roles through the Enterprise Application to grant appropriate permissions.

### 8. Pre-authorize Client Applications (If UI App Exists)

> **Note**: Only perform this step if you have already created the UI application and have its Application ID.

1. Go to **Expose an API** tab
2. Click **Add a client application**
3. Enter the **UI Application ID**
4. Check the **user_impersonation** scope
5. Click **Add application**

### 9. Create Application Secret (Optional - only if using client secret authentication)

1. Go to **Certificates & secrets** tab
2. Click **New client secret**
3. Configure:
   - **Description**: `WebAPI Secret`
   - **Expires**: Choose appropriate expiration (e.g., 24 months)
4. Click **Add**
5. **Important**: Copy the secret value immediately (it won't be shown again)
6. Input the secret when the deployment script prompts you for it. 

### 10. Create Service Principal

#### Method 1: Azure Portal (Automatic)
The service principal is automatically created when you first access certain sections of your app registration:

1. In your **App registration**, go to the **Overview** tab
2. Click on **Managed application in local directory** link
   - This will automatically create the service principal and take you to the Enterprise Applications view
3. Alternatively, go to **Microsoft Entra ID** > **Enterprise applications**
4. Search for your application name: `{SolutionAbbreviation}-webapi-{EnvironmentAbbreviation}`
   - Example: `gmm-webapi-prod`
5. If it appears in the list, the service principal already exists

#### Method 2: Azure Portal (Manual Creation)
If the service principal doesn't exist automatically:

1. Go to **Microsoft Entra ID** > **Enterprise applications**
2. Click **New application**
3. Click **Create your own application**
4. Select **Register an application to integrate with Azure AD (App you're developing)**
   - Use the same name as the app registration: `{SolutionAbbreviation}-webapi-{EnvironmentAbbreviation}`
5. Search for and select your existing app registration: `{SolutionAbbreviation}-webapi-{EnvironmentAbbreviation}`
   - Example: `gmm-webapi-prod`
6. This will create the corresponding service principal

### 11. Verification Checklist

Verify your application has the following configuration:

- [ ] **Display Name**: `{SolutionAbbreviation}-webapi-{EnvironmentAbbreviation}`
- [ ] **Sign-in Audience**: `AzureADMyOrg`
- [ ] **Public Client**: Disabled (`isFallbackPublicClient: false`)
- [ ] **Redirect URI**: Swagger OAuth2 redirect URL configured
- [ ] **Implicit Grant**: Both access tokens and ID tokens enabled
- [ ] **Application ID URI**: `api://{ApplicationId}`
- [ ] **OAuth2 Scope**: `user_impersonation` scope created
- [ ] **Access Token Version**: Set to `2`
- [ ] **API Permissions**: Microsoft Graph `User.Read` with admin consent
- [ ] **Optional Claims**: `upn` claim in access tokens
- [ ] **Application Roles**: All 13 roles created and enabled
- [ ] **Pre-authorized Apps**: UI application
- [ ] **Client Secret**: Created and securely stored (if applicable)
- [ ] **Service Principal**: Created


## Store Secrets in Key Vault

The deployment script will prompt you to input the application secret if you are using 'ClientSecret' authentication. 

### Assigning Users to Roles

After creating the application and roles:

1. Go to **Microsoft Entra ID** > **Enterprise applications**
2. Search for and select your Web API application
3. Go to **Users and groups**
4. Click **Add user/group**
5. Select users or groups to assign
6. Select the appropriate application role(s)
7. Click **Assign**

## Common Issues and Troubleshooting

### Issue: "Invalid redirect URI"
- **Solution**: Ensure the redirect URI matches exactly: `https://{solution}-compute-{env}-webapi.azurewebsites.net/swagger/oauth2-redirect.html`

### Issue: "Access token version not accepted"
- **Solution**: Verify `accessTokenAcceptedVersion` is set to `2` in the application manifest

### Issue: "Insufficient privileges"
- **Solution**: Ensure you have Application Administrator or Global Administrator permissions

### Issue: "Scope not found"
- **Solution**: Verify the OAuth2 permission scope `user_impersonation` is created and enabled

### Issue: "Pre-authorization not working"
- **Solution**: Ensure the UI Application ID is correct and the scope is properly selected

### Issue: "Cannot create app role"
- **Solution**: Ensure the **Value** field is unique and follows the correct naming pattern. If updating existing roles, you may need to disable the old role first before creating a new one with the same value.

### Issue: "App roles not appearing in token"
- **Solution**: Ensure users/groups are assigned to the roles in the Enterprise Application, and that the roles are enabled.

## Integration with GMM

Once created, this Web API application will:

1. **Authenticate users** accessing the GMM web interface
2. **Authorize API calls** from the UI application using application roles
3. **Provide secure access** to GMM backend services with role-based permissions
4. **Enable token-based authentication** for service-to-service communication
5. **Enforce role-based access control** for different GMM operations


---

**Note**: These instructions are based on the configuration created by `Set-WebApiAzureADApplication.ps1` and `Set-AppRolesIfNeeded.ps1`. For automated setup, use the PowerShell scripts instead of manual configuration.
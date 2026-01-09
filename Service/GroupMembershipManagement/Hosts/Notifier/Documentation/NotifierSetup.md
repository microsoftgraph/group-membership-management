# Notifier Setup

The GMM Notifier uses adaptive cards to send Outlook Actionable Emails (OAM). OAM requires that a special id, called a `provider id`, be included in each adaptive card payload in order to validate the sender, content, and recipients of the message.

If an actionable message is sent from an email address that is not part of the approved senders list, the adaptive card will not be rendered.

These are the steps to procure a `provider id` for an environment.

## Create OAM Entra App

1. Navigate to the [Azure Portal] and go to **Azure Active Directory** > **App registrations**.
1. Click **New registration** and provide:
    - **Name**: A descriptive name for the app. *Example: `<SolutionAbbreviation>`-OAM-`<EnvironmentAbbreviation>`.*
    - **Supported account types**: Select "Accounts in this organizational directory only (Single tenant)".
    - **Redirect URI**: Leave blank for now.
1. Click **Register**.
1. After the app is created, note the **Application (client) ID** - you'll need this for the `oamEntraAppId` parameter.
1. Navigate to **Expose an API** and click **Add a scope**:
    - **Scope name**: `Global`
    - **Who can consent**: Admins and users
    - **Admin consent display name**: Access OAM application
    - **Admin consent description**: Allows the app to access OAM functionality on behalf of the signed-in user
    - **State**: Enabled
1. Click **Add scope**.
1. Still in **Expose an API**, scroll down to **Authorized client applications** and click **Add a client application**:
    - **Client ID**: Enter the Outlook client application ID: `48af08dc-f6d2-435f-b2a7-069abd99c086`
    - **Authorized scopes**: Check the scope you just created (e.g., `api://auth-am-<oam-provider-id>/<your-app-id>/Global`)
1. Click **Add application**.

## Create a Provider

1. Navigate to the [Outlook Actionable Messages Developer Dashboard].
1. Click the 'New Provider' button.
1. Fill out the required fields:
    Field Name | Description
    -|-
    Friendly Name | A name that represents the environment associated with the Originator Id. *Example: `<SolutionAbbreviation>` Notifier `<EnvironmentAbbreviation>`.*
    Sender Email Address | The email address from which actionable messages will be sent.
    Target URLs | The endpoints where the actional messages will send responses. Example: https://`<SolutionAbbreviation>`-compute-`<EnvironmentAbbreviation>`-webapi.azurewebsites.net/.+
    Scope of Submission | This should be set to `Organization` scope.

After the submission is sent, an email will be sent to the Exchange admins asking them to review the request. The email contains an actionable message with a button that directs them to the [Outlook Actionable Messages Admin Dashboard] where they can approve the request.


## Update parameters.env.json file with your new notifierId

1. Find your *parameters.`<EnvironmentAbbreviation>`.json* file in the [Infrastructure/data/parameters directory](../Infrastructure/data/parameters/)
1. Update the "notifierProviderId" parameter with the value of the Provider Id (Originator) from the provider that you created
1. Update the "oamEntraAppId" parameter with the Application (client) ID from the Entra app you created
1. Update the "oamEntraAppScope" parameter if needed (default is "Global")
1. Add and commit the change to the repository so that it gets written to the data keyvault

<!-- Link References -->
[Azure Portal]: https://portal.azure.com
[Outlook Actionable Messages Developer Dashboard]: https://aka.ms/publishoam
[Outlook Actionable Messages Admin Dashboard]: https://outlook.office.com/connectors/oam/Admin
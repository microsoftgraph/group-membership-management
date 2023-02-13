# Notifier Setup

The GMM Notifier uses adaptive cards to send Outlook Actionable Emails (OAM). OAM requires that an special id, called an `originator id`, be included in each adaptive card payload in order to validate the sender, content, and recipients of the message.

The originator id is not a secret, since it is only one part of a multipart authorization scheme. The originator id is tied directly to a list of authorized senders, from which the email must be sent in order to be accepted.  Regardless, we will be treating it like a secret within GMM.

These are the steps to procure , you must procure an `originator id` for your environment.

## Prerequisites

1. You should already have set up your pre-requisites Key Vault.
2. You need a list of sender email addresses from which actionable emails will originate.

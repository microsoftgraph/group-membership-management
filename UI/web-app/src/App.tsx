// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { MsalProvider } from "@azure/msal-react";
import { IPublicClientApplication } from "@azure/msal-browser";
import PageLayout from "./components/PageLayout";

type AppProps = {
    pca: IPublicClientApplication
};

function App({ pca }: AppProps) {
    return (
      <MsalProvider instance={pca}>
        <PageLayout />
      </MsalProvider>
    );
}

export default App;
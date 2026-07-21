// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.Helpers
{
    public static class UiUrlBuilder
    {
        // Builds an absolute GMM UI job link; takeAction adds ?takeAction=true to auto-open the Threshold Exceeded panel (FR-002).
        public static string BuildJobDetailsUrl(string uiUrl, Guid syncJobId, bool includeHistory = false, bool takeAction = false)
        {
            if (!Uri.TryCreate(uiUrl, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                return string.Empty;
            }

            var pathSuffix = includeHistory ? "/history" : string.Empty;
            var uriBuilder = new UriBuilder(baseUri)
            {
                Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/jobdetails/{syncJobId}{pathSuffix}",
                Query = takeAction ? "takeAction=true" : string.Empty,
                Fragment = string.Empty
            };

            return uriBuilder.Uri.AbsoluteUri;
        }
    }
}

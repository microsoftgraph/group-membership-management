// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class GMMResources : IGMMResources
    {
        public GMMResources()
        {
        }

        public GMMResources(string learnMoreAboutGMMUrl)
        {
            this.LearnMoreAboutGMMUrl = learnMoreAboutGMMUrl;
        }

        public string LearnMoreAboutGMMUrl { get; set; }
    }
}

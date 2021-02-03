// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IEmailSender
    {
        string Email { get; }
        string Password { get; }
    }
}

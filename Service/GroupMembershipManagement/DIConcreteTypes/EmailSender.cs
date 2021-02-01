// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class EmailSender : IEmailSender
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public EmailSender(string email, string password)
        {
            Email = email;
            Password = password;
        }

        public EmailSender()
        {

        }
    }
}

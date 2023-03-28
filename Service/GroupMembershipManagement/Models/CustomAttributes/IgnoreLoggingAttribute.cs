// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Models.CustomAttributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class IgnoreLoggingAttribute : Attribute
    {
    }
}

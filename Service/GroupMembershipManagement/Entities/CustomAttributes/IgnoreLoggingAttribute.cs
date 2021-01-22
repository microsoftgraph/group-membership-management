// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Entities.CustomAttributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class IgnoreLoggingAttribute : Attribute
    {
    }
}

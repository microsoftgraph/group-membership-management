// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text;

namespace Hosts.AzureUserReader
{
    public class PasswordGenerator
    {
        public static string GeneratePassword()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++)
                sb.Append(CharacterBetween('A', 'Z'));
            for (int i = 0; i < 8; i++)
                sb.Append(CharacterBetween('a', 'z'));
            for (int i = 0; i < 8; i++)
                sb.Append(CharacterBetween('0', '9'));
            for (int i = 0; i < 8; i++)
                sb.Append(CharacterIn("@#$%^&*-_!+=[]{}| \\:',.?/`~\"();"));
            Shuffle(sb);
            return sb.ToString();
        }

        private static readonly Random _random = new Random();
        private static char CharacterBetween(char begin, char end)
        {
            return (char)_random.Next(begin, end + 1);
        }

        private static char CharacterIn(string str)
        {
            return str[_random.Next(0, str.Length)];
        }

        private static void Shuffle(StringBuilder sb)
        {
            for (int i = 0; i < sb.Length; i++)
            {
                int toswap = _random.Next(i, sb.Length);
                char temp = sb[i];
                sb[i] = sb[toswap];
                sb[toswap] = temp;
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.IO;

namespace Console
{
	class DemoData
	{
		private readonly StreamReader _file;
		private readonly List<EmailIDPair> _readSoFar = new List<EmailIDPair>();

		public DemoData()
		{
			_file = File.OpenText("data.csv");
			_file.ReadLine(); // discard the header line
		}

		public EmailIDPair GetMockUserInfo(int index)
		{
			while (_readSoFar.Count < index + 1 && !_file.EndOfStream)
			{
				var nextLine = _file.ReadLine().Split(',');
				_readSoFar.Add(new EmailIDPair { ImmutableId = nextLine[0], 
					Alias = (nextLine.Length == 1 || string.IsNullOrWhiteSpace(nextLine[1])) ? $"testuser{index}" : nextLine[1] });
			}

			return _readSoFar[index];
		}
	}

	class EmailIDPair
	{
		public string ImmutableId;
		public string Alias;
	}
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.IO;

namespace DemoUserSetup
{

	// if you want to quickly generate a CSV file for this, try this powershell one-liner:
	// 0..250000 | ForEach-Object { [PSCustomObject]@{ImmutableId=$_;Email="TESTUSER$_"} } | Export-Csv -NoTypeInformation data.csv
	// replace 250000 with the number of users you want, of course

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
				_readSoFar.Add(new EmailIDPair { ImmutableId = nextLine[0].Trim('"'), 
					Alias = (nextLine.Length == 1 || string.IsNullOrWhiteSpace(nextLine[1])) ? $"testuser{index}" : nextLine[1].Trim('"') });
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

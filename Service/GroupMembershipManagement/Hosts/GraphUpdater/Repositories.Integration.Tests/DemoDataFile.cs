using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Repositories.Integration.Tests
{
	class DemoDataFile
	{
		private readonly StreamReader _file;
		private readonly List<EmailIDPair> _readSoFar = new List<EmailIDPair>();

		public DemoDataFile()
		{
			_file = File.OpenText("DemoUserData.csv");
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

	class DemoData
	{
		private readonly List<EmailIDPair> _generated = new List<EmailIDPair>();
		private readonly Random _random = new Random();
		public EmailIDPair GetMockUserInfo(int index)
		{
			while (_generated.Count < index + 1)
			{
				_generated.Add(new EmailIDPair { ImmutableId = RandomString('0', '9', 2, 9), Alias = RandomString('A', 'Z', 5, 12) });
			}

			return _generated[index];
		}

		private string RandomString(char start, char end, int minLength, int maxLength)
		{
			return string.Concat(Enumerable.Range(0, _random.Next(minLength, maxLength + 1)).Select(_ => (char)_random.Next((int)start, (int)end + 1)));
		}
	}

	class EmailIDPair
	{
		public string ImmutableId;
		public string Alias;
	}
}

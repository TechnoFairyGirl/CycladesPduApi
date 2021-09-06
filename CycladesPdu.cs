using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CycladesPduApi
{
	class CycladesPdu
	{
		public static T Retry<T>(int times, Func<int, T> func)
		{
			for (var i = 1; ; i++)
			{
				try { return func(i); }
				catch { if (times > 0 && i >= times) throw; }
			}
		}

		public static void Retry(int times, Action<int> func) =>
			Retry(times, i => { func(i); return 0; });

		public string SerialPort { get; }
		public string User { get; }
		public string Password { get; }

		public CycladesPdu(string serialPort, string user = "admin", string password = "pm8")
		{
			SerialPort = serialPort;
			User = user;
			Password = password;
		}

		Task commandDelay = Task.CompletedTask;

		public string[] DoCommand(string command, out int outletCount)
		{
			try
			{
				commandDelay.Wait();

				using var port = new SerialPort(SerialPort);

				port.DtrEnable = true;
				port.RtsEnable = true;

				port.ReadTimeout = 5000;

				string[] ReadLinesTo(string str)
				{
					port.NewLine = "\r\n";
					return port.ReadTo($"\r\n{str}").Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
				}

				void WriteLine(string str)
				{
					port.NewLine = "\r";
					port.WriteLine(str);
				}

				string[] WriteCommand(string cmd, string delimiter = "pm>")
				{
					WriteLine(cmd);
					return ReadLinesTo(delimiter);
				}

				port.Open();

				var banner = ReadLinesTo("Username: ");
				var lastOutlet = int.Parse(banner[4].Split(' ').Last());

				outletCount = lastOutlet;

				if (command == null) return null;

				WriteCommand(User, "Password: ");
				WriteCommand(Password);

				var waitForChainsTimeout = Task.Delay(20000);

				while (true)
				{
					if (waitForChainsTimeout.IsCompleted) throw new TimeoutException();
					var status = WriteCommand($"status {lastOutlet}").Last();
					if (status.Contains("ON") || status.Contains("OFF")) break;
				}

				var result = WriteCommand(command);

				return result;
			}
			finally { commandDelay = Task.Delay(1000); }
		}

		public string[] DoCommand(string command) => DoCommand(command, out _);

		public bool GetOutletState(int outlet)
		{
			if (outlet < 1) throw new ArgumentOutOfRangeException("outlet");
			var result = DoCommand($"status {outlet}", out var outletCount);
			if (outlet > outletCount) throw new ArgumentOutOfRangeException("outlet");

			foreach (var line in result)
			{
				var match = Regex.Match(line, @"\t[^\t]*(ON|OFF)\t");
				if (!match.Success) continue;

				return match.Groups[1].Value == "ON";
			}

			throw new InvalidOperationException();
		}

		public void SetOutletState(int outlet, bool state)
		{
			string onOff = state ? "on" : "off";

			if (outlet < 1) throw new ArgumentOutOfRangeException("outlet");
			var result = DoCommand($"{onOff} {outlet}", out var outletCount);
			if (outlet > outletCount) throw new ArgumentOutOfRangeException("outlet");

			if (!result.Any(line => line.EndsWith($"Outlet turned {onOff}.")))
				throw new InvalidOperationException();
		}

		public int GetOutletCount()
		{
			DoCommand(null, out var outletCount);
			return outletCount;
		}
	}
}

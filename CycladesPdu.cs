using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CycladesPduApi
{
	sealed class CycladesPdu
	{
		static T Retry<T>(int times, int delay, Func<int, T> func)
		{
			for (var i = 1; ; i++)
			{
				try { return func(i); }
				catch { if (times > 0 && i >= times) throw; }
				Task.Delay(delay).Wait();
			}
		}

		static void Retry(int times, int delay, Action<int> func) =>
			Retry(times, delay, i => { func(i); return 0; });

		public string SerialPort { get; }
		public string User { get; }
		public string Password { get; }
		public int OutletCount { get; private set; }
		
		SerialPort port = null;
		bool connected = false;
		object lockObject = new object();

		public bool Connected { get => port != null && port.IsOpen && connected; }

		public CycladesPdu(string serialPort, string user = "admin", string password = "pm8")
		{
			SerialPort = serialPort;
			User = user;
			Password = password;
			OutletCount = 0;
		}

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

		bool TestConnection()
		{
			var status = WriteCommand($"status {OutletCount}").Last();
			return status.Contains("ON") || status.Contains("OFF");
		}

		public void Connect() => Connect(false, 5);

		void Connect(bool reconnect, int retryTimes)
		{
			try { Retry(retryTimes, 1000, i => ConnectInternal(reconnect)); }
			catch { Disconnect(); throw; }
		}

		void ConnectInternal(bool reconnect)
		{
			lock (lockObject)
			{
				if (port != null)
				{
					port.Dispose();
					Task.Delay(1000).Wait();
				}

				if (reconnect && !connected) return;

				port = new SerialPort(SerialPort, 9600);

				port.DtrEnable = true;
				port.RtsEnable = true;

				port.ReadTimeout = 1000 * 5;

				port.Open();

				var banner = ReadLinesTo("Username: ");

				OutletCount = int.Parse(banner[4].Split(' ').Last());

				WriteCommand(User, "Password: ");
				WriteCommand(Password);

				var waitForChainsTimeout = Task.Delay(1000 * 20);

				while (true)
				{
					if (waitForChainsTimeout.IsCompleted) throw new TimeoutException();
					if (TestConnection()) break;
				}

				connected = true;

				var portCopy = port;

				Task.Run(() =>
				{
					try
					{
						while (true)
						{
							lock (lockObject)
							{
								if (!portCopy.IsOpen) break;
								if (!TestConnection()) throw new InvalidOperationException();
							}

							Task.Delay(1000 * 30).Wait();
						}
					}
					catch { Connect(true, -1); }
				});
			}
		}

		public void Disconnect()
		{
			lock (lockObject)
			{
				connected = false;
				if (port != null)
				{
					port.Dispose();
					Task.Delay(1000).Wait();
				}
			}
		}

		public string[] DoCommand(string command)
		{
			lock (lockObject)
			{
				if (!Connected)
					throw new InvalidOperationException();

				return WriteCommand(command);
			}
		}

		public bool GetOutletState(int outlet)
		{
			if (outlet < 1 || outlet > OutletCount)
				throw new ArgumentOutOfRangeException("outlet");

			var result = DoCommand($"status {outlet}");

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
			if (outlet < 1 || outlet > OutletCount)
				throw new ArgumentOutOfRangeException("outlet");

			string onOff = state ? "on" : "off";

			var result = DoCommand($"{onOff} {outlet}");

			if (!result.Any(line => line.EndsWith($"Outlet turned {onOff}.")))
				throw new InvalidOperationException();
		}
	}
}

using Newtonsoft.Json;
using SimpleHttp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CycladesPduApi
{
	sealed class Config
	{
		public class PortMapping
		{
			public string Endpoint { get; set; }
			public string SerialDevice { get; set; }
		}

		public int HttpPort { get; set; }
		public string HttpToken { get; set; }
		public PortMapping[] Ports { get; set; }
	}

	static class Program
	{
		static Config config;

		static void Main(string[] args)
		{
			var exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
			var configPath = args.Length >= 1 ? args[0] : Path.Combine(Path.GetDirectoryName(exePath), "config.json");
			config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));

			var server = new HttpServer(config.HttpPort);

			server.AddRoute(null, null, (urlArgs, request, response) =>
			{
				if (config.HttpToken != null && request.Headers["authorization"] != $"Bearer {config.HttpToken}")
					throw new UnauthorizedAccessException();

				return true;
			});

			foreach (var portMapping in config.Ports)
			{
				var endpoint = portMapping.Endpoint;
				var pdu = new CycladesPdu(portMapping.SerialDevice);

				pdu.Connect();

				server.Log($"Connected to PDU on '{pdu.SerialPort}'. ({pdu.OutletCount} outlets)");

				server.AddExactRoute("GET", $"/{endpoint}/outlets", (request, response) =>
				{
					int outletCount;
					lock (pdu) outletCount = pdu.OutletCount;
					response.WriteBodyJson(outletCount);
				});

				server.AddRoute("GET", $@"/{endpoint}/outlet/(\d+)", (urlArgs, request, response) =>
				{
					var outlet = int.Parse(urlArgs[0]);
					bool outletState;
					lock (pdu) outletState = pdu.GetOutletState(outlet);
					response.WriteBodyJson(outletState);
				});

				server.AddRoute("POST", $@"/{endpoint}/outlet/(\d+)", (urlArgs, request, response) =>
				{
					var outlet = int.Parse(urlArgs[0]);
					var state = request.ReadBodyJson<bool>();
					lock (pdu) pdu.SetOutletState(outlet, state);

					server.Log($"Outlet {outlet} turned {(state ? "on" : "off")}.");
				});

				server.AddExactRoute("GET", $@"/{endpoint}/outlet/all", (request, response) =>
				{
					bool[] outletStates;
					lock (pdu) outletStates = pdu.GetAllOutletStates();
					response.WriteBodyJson(outletStates);
				});
			}

			server.Start();

			Task.Delay(-1).Wait();
		}
	}
}

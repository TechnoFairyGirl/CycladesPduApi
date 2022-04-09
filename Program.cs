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

			var portMappings = config.Ports.ToDictionary(
				portMapping => portMapping.Endpoint,
				portMapping => new CycladesPdu(portMapping.SerialDevice));

			var server = new HttpServer(config.HttpPort);

			server.AddRoute(null, null, (urlArgs, request, response) =>
			{
				if (config.HttpToken != null && request.Headers["authorization"] != $"Bearer {config.HttpToken}")
					throw new UnauthorizedAccessException();

				return true;
			});

			foreach (var portMapping in portMappings)
			{
				var endpoint = portMapping.Key;
				var pdu = portMapping.Value;

				pdu.Connect();

				server.Log($"Connected to PDU on '{pdu.SerialPort}'. ({pdu.OutletCount} outlets)");

				server.AddExactRoute("GET", $"/{endpoint}/outlets", (request, response) =>
				{
					lock (pdu) response.WriteBodyJson(pdu.OutletCount);
				});

				server.AddRoute("GET", $@"/{endpoint}/outlet/(\d+)", (urlArgs, request, response) =>
				{
					var outlet = int.Parse(urlArgs[0]);
					lock (pdu) response.WriteBodyJson(pdu.GetOutletState(outlet));
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
					lock (pdu) response.WriteBodyJson(pdu.GetAllOutletStates());
				});
			}

			server.Start();

			Task.Delay(-1).Wait();
		}
	}
}

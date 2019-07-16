using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketServer
{
	class Program
	{
		static TcpListener TcpListener;
		static List<TcpClient> TcpClients = new List<TcpClient>();

		static void Main(string[] args)
		{
			TcpListener = new TcpListener(IPAddress.Any, 80);
			TcpListener.Start();

			TcpListener.BeginAcceptTcpClient(listener_AcceptTcpClient, null);

			Console.WriteLine("Server listening on port 80.  Press ENTER to shutdown.");
			Console.ReadLine();

			TcpListener.Stop();

			TcpClients.ForEach(x => x.Close());
		}

		private static void listener_AcceptTcpClient(IAsyncResult result)
		{
			var client = TcpListener.EndAcceptTcpClient(result);

			TcpListener.BeginAcceptTcpClient(listener_AcceptTcpClient, null);

			SendHandshake(client);
		}

		private static void SendHandshake(TcpClient client)
		{
			var stream = client.GetStream();

			var reader = new StreamReader(stream);

			var request = reader.ReadLine();

			if (request == null)
				return;

			var headers = new Dictionary<string, string>();

			while (true)
			{
				var line = reader.ReadLine();

				if (line == string.Empty)
					break;

				var keyLength = line.IndexOf(':');

				headers[line.Substring(0, keyLength)] = line.Substring(keyLength+1).Trim();
			}

			// Get Key
			if (!headers.TryGetValue("Sec-WebSocket-Key", out string key))
			{
				client.Close();
				TcpClients.Remove(client);

				return;
			}

			var writer = new StreamWriter(stream);

			writer.WriteLine("HTTP/1.1 101 Switching Protocols");
			writer.WriteLine("Connection: Upgrade");
			writer.WriteLine("Upgrade: websocket");
			writer.Write("Sec-WebSocket-Accept: ");
			using (var algorithm = SHA1.Create())
				writer.WriteLine(Convert.ToBase64String(algorithm.ComputeHash(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))));
			writer.WriteLine();

			writer.Flush();

			TcpClients.Add(client);

			while (true)
			{
				while (client.Available != 0)
				{
					var header = stream.ReadByte();
					var header2 = stream.ReadByte();

					var length = header2 & 0x7f;

					if (length == 126)
					{
						var length2 = new byte[2];

						stream.Read(length2, 0, 2);

						length = BitConverter.ToInt16(length2, 0);
					}
					else if (length == 127)
					{
						var length2 = new byte[8];

						stream.Read(length2, 0, 8);

						length = BitConverter.ToInt32(length2, 0);
					}

					var mask = new byte[4];

					stream.Read(mask, 0, mask.Length);

					var data = new byte[length];

					stream.Read(data, 0, length);

					for (var i = 0; i < length; i++)
						data[i] ^= mask[i % 4];

					var opcode = header & 0xf;

					switch (opcode)
					{
						case 1:
							Console.WriteLine(Encoding.UTF8.GetString(data));

							header2 &= 0x7f;

							foreach (var client2 in TcpClients)
							{
								var stream2 = client2.GetStream();

								stream2.WriteByte((byte)header);
								stream2.WriteByte((byte)header2);
								//stream.Write(mask, 0, mask.Length);

								//for (var i = 0; i < length; i++)
								//	data[i] ^= mask[i % 4];

								stream2.Write(data, 0, data.Length);
								stream2.Flush();
							}
							break;

						case 8:
							Console.WriteLine("Disconnected");

							client.Close();
							TcpClients.Remove(client);
							return;
					}
				}

				Thread.Sleep(50);
			}
		}
	}
}

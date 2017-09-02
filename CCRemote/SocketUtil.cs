using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace CCRemote
{
	/// <summary>
	/// 处理网络请求的工具类
	/// </summary>
	public partial class SocketUtil
	{
		#region Constants

		// UDP请求和对应的回应
		private const string UDP_REQUEST = "nya?";
		private const string UDP_RESPONSE = "nya!";

		// TCP缓冲区大小
		private const int TCP_BUFFER_SIZE = 1024;

		// TCP收到的消息中，前　TCP_SIZE_LENGTH　字节表示该消息的总长度
		// 后 TCP_NUM_LENGTH 字节表示消息编号
		// 之后　TCP_HEAD_LENGTH　字节表示消息头
		// 剩余为消息内容
		private const int TCP_SIZE_LENGTH = 4;
		private const int TCP_NUM_LENGTH = 4;
		private const int TCP_HEAD_LENGTH = 4;
		
		#endregion

		#region Filed

		private readonly int port;

		#endregion

		#region Constructor

		public SocketUtil(int port)
		{
			this.port = port;
		}

		#endregion

		#region Udp Method

		/// <summary>
		/// 侦听并回应Udp广播，使得服务端能被客户端发现
		/// </summary>
		public void UdpListener()
		{
			UdpClient client;
			IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, port);
			byte[] responseBytes = Encoding.UTF8.GetBytes(UDP_RESPONSE + Dns.GetHostName());
			using (client = new UdpClient(port))
			{
				while (true)
				{
					Byte[] bytes = client.Receive(ref remoteEP);
					String str = Encoding.UTF8.GetString(bytes);
					if (str == UDP_REQUEST)
					{
						client.Send(responseBytes, responseBytes.Length, remoteEP);
					}
				}
			}
		}

		#endregion

		#region Tcp Methods

		/// <summary>
		/// 接受新的Tcp客户端并为其安排子线程处理
		/// </summary>
		public void TcpListener()
		{
			TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
			listener.Start();
			while (true)
			{
				TcpClient client = listener.AcceptTcpClient();
				Task.Factory.StartNew(TcpHandle, client);
			}
		}

		/// <summary>
		/// 接受一个客户端的请求消息，并作回应
		/// </summary>
		private void TcpHandle(Object tcpClient)
		{
			TcpClient client;
			using (client = tcpClient as TcpClient)
			{
				var ns = client.GetStream();
				byte[] buffer = new byte[TCP_BUFFER_SIZE];
				List<byte> request; // 编号 + 头 + 内容
				int count;
				while (true)
				{
					#region Get request

					count = ns.Read(buffer, 0, TCP_BUFFER_SIZE);
					if (count < TCP_SIZE_LENGTH)
						continue; // 鬼知道发生了什么，当没看到
					request = new List<byte>();
					for (int i = TCP_SIZE_LENGTH; i < count; i++) request.Add(buffer[i]);
					int size = buffer.GetInt();
					int remain = size - count;
					while (remain > 0) // 一个缓冲区装不下的话多装几次
					{
						count = ns.Read(buffer, 0, TCP_BUFFER_SIZE);
						for (int i = 0; i < count; i++) request.Add(buffer[i]);
						remain -= count;
					}
					if (request.Count < TCP_NUM_LENGTH + TCP_HEAD_LENGTH)
						continue; // 继续见鬼

					#endregion

					Task.Factory.StartNew((o) => 
					{
						ResponseThread thread = o as ResponseThread;
						List<byte> body = thread.GetResponse(); // 回应的内容
						if (body != null)
						{
							// 回应的格式为 长度 + 编号 + 内容
							int length = TCP_SIZE_LENGTH + TCP_NUM_LENGTH + body.Count;
							List<byte> response = new List<byte>();
							response.AddInt(length);
							response.AddInt(thread.Number);
							response.AddRange(body);

							ns.Write(response.ToArray(), 0, response.Count);
						}
					}, new ResponseThread(request));
				}
			}
		}

		#endregion
	}
}

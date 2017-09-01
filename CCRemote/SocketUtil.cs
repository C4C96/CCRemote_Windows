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
	public class SocketUtil
	{
		#region Constants

		// UDP请求和对应的回应
		private const String UDP_REQUEST = "nya?";
		private const String UDP_RESPONSE = "nya!";

		// TCP缓冲区大小
		private const int TCP_BUFFER_SIZE = 1024;

		//　TCP的消息中，前　TCP_SIZE_LENGTH　字节表示该消息的总长度
		//　后　TCP_HEAD_LENGTH　字节表示消息头
		//　剩余为消息内容
		private const int TCP_SIZE_LENGTH = 4;
		private const int TCP_HEAD_LENGTH = 4;

		// Tcp请求头的类型
		private const int GET_FILE_SYSTEM_ENTRIES = 233;
		
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
				List<byte> request; // 头 + 内容
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
					if (request.Count < TCP_HEAD_LENGTH)
						continue; // 继续见鬼

					#endregion
					
					// TODO: 计算并发送回应可以改成并行，但要注意request需传值
					List<byte> response = GetTcpResponse(request); // 头 + 内容
					List<byte> sendBytes = new List<byte>(response.Count + TCP_SIZE_LENGTH); // 发送的内容（长度 + 头 + 内容）
					sendBytes.AddInt(response.Count + TCP_SIZE_LENGTH);
					sendBytes.AddRange(response);

					ns.Write(sendBytes.ToArray(), 0, sendBytes.Count);			
				}
			}
		}

		/// <summary>
		/// 处理一条Tcp请求
		/// </summary>
		/// <param name="request">收到的请求的头+内容</param>
		/// <returns>需要返回的头+内容</returns>
		private List<byte> GetTcpResponse(List<byte> request)
		{
			int head = request.GetInt();
			List<byte> body = request.GetRange(TCP_HEAD_LENGTH, request.Count - TCP_HEAD_LENGTH);
			switch (head)
			{
				case GET_FILE_SYSTEM_ENTRIES:
					return Tcp_GetFileSystemEntries(body);
				default:
					return new List<byte>();
			}
		}

		#region Tcp Response Method

		/// <summary>
		/// 对于目录内容的回应
		/// 回应的格式：头 + (属性 + 路径字节数 + 路径) * N
		///					4字节		4字节
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		private List<byte> Tcp_GetFileSystemEntries(List<byte> request)
		{
			const int MASK = (int) (FileAttributes.Hidden | FileAttributes.System);

			List<byte> response = new List<byte>();
			response.AddInt(GET_FILE_SYSTEM_ENTRIES);

			string directoryPath = Encoding.UTF8.GetString(request.ToArray());
			string[] pathes = Directory.GetFileSystemEntries(directoryPath);
			foreach (var path in pathes)
			{
				int attributes = (int)File.GetAttributes(path);
				if ((attributes & MASK) == MASK)
					continue; // 略过系统隐藏文件，如$RECYCLE.BIN
				byte[] pathBytes = Encoding.UTF8.GetBytes(path);

				response.AddInt(attributes);
				response.AddInt(pathBytes.Length);
				response.AddRange(pathBytes);
			}
			return response;
		}

		#endregion

		#endregion
	}
}

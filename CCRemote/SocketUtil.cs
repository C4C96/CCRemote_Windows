using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace CCRemote
{
	/// <summary>
	/// 处理网络请求的工具类
	/// </summary>
	public partial class SocketUtil
	{
		#region Static Members

		public static int Port { get; set; }
		public static SocketUtil Instance
		{
			get
			{
				if (instance == null)
					instance = new SocketUtil(Port);
				return instance;
			}
		}
		private static SocketUtil instance;

		#endregion

		#region Constants

		// UDP请求和对应的回应
		private const string UDP_REQUEST = "nya?";
		private const string UDP_RESPONSE = "nya!";

		// TCP缓冲区大小
		private const int TCP_BUFFER_SIZE = 1024;
		
		// 心跳间隔
		private const int HEART_BEAT_DELAY = 1500;
		// 心跳编号
		private const int HEART_BEAT_NUMBER = -1;

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
		private ConcurrentBag<AsyncOperation> aoList;

		#endregion

		#region Constructor

		private SocketUtil(int port)
		{
			this.port = port;
			aoList = new ConcurrentBag<AsyncOperation>();
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
				// TODO 新接入
				Console.WriteLine(client.Client.RemoteEndPoint + " connected.");
				Task.Factory.StartNew(TcpHandle, client);
			}
		}

		/// <summary>
		/// 接受一个客户端的请求消息，并作回应
		/// </summary>
		private void TcpHandle(Object tcpClient)
		{
			TcpClient client = tcpClient as TcpClient;
			try
			{
				Task.Factory.StartNew(HeartBeat, client);

				var ns = client.GetStream();
				byte[] buffer = new byte[TCP_BUFFER_SIZE];
				List<byte> request; // 编号 + 头 + 内容
				int count;
				while (true)
				{
					#region Get request

					count = ns.Read(buffer, 0, TCP_BUFFER_SIZE);
					if (count <= TCP_SIZE_LENGTH)
						continue; // 忽略过短的非法请求
					int size = buffer.GetInt();
					if (size <= TCP_SIZE_LENGTH)
						continue;
					request = new List<byte>();
					for (int i = TCP_SIZE_LENGTH; i < count; i++) request.Add(buffer[i]);					
					int remain = size - count;
					while (remain > 0) // 一个缓冲区装不下的话多装几次
					{
						count = ns.Read(buffer, 0, TCP_BUFFER_SIZE);
						for (int i = 0; i < count; i++) request.Add(buffer[i]);
						remain -= count;
					}
					if (request.Count < TCP_NUM_LENGTH + TCP_HEAD_LENGTH)
						continue; // 忽略

					#endregion

					Task.Factory.StartNew((o) => 
					{
						ResponseThread thread = o as ResponseThread;
						List<byte> body = thread.GetResponse(); // 回应的内容
						if (body != null)
							Send(ns, thread.Number, body);
					}, new ResponseThread(request));
				}
			}
			catch (IOException) // Read抛出异常，此处无需client.Close()，会在心跳线程中完成
			{
				// TODO 断开
				Console.WriteLine(client.Client.RemoteEndPoint + " disconnected.");
			}
		}

		/// <summary>
		/// 发送信息
		/// 格式为：长度 + 编号 + 内容
		///			4字节  4字节
		/// </summary>
		/// <param name="ns"></param>
		/// <param name="number"></param>
		/// <param name="body"></param>
		private void Send(NetworkStream ns, int number, List<byte> body)
		{
			int length = TCP_SIZE_LENGTH + TCP_NUM_LENGTH + body.Count;
			List<byte> response = new List<byte>();
			response.AddInt(length);
			response.AddInt(number);
			response.AddRange(body);

			ns.Write(response.ToArray(), 0, response.Count);
		}

		/// <summary>
		/// 不断发送心跳请求，发现连接中断则释放资源
		/// </summary>
		/// <param name="tcpClient"></param>
		private void HeartBeat(Object tcpClient)
		{
			TcpClient client = tcpClient as TcpClient;
			try
			{
				var ns = client.GetStream();
				while (true)
				{
					List<byte> response = new List<byte>();
					foreach (var ao in aoList)
						response.AddAsyncOperation(ao);
					Send(ns, HEART_BEAT_NUMBER, response);
					Thread.Sleep(HEART_BEAT_DELAY);
				}
			}
			catch (Exception)
			{
				client.Close();
			}
		}

		#endregion

		/// <summary>
		/// 对请求异步作出回应的类，并不涉及网络链接
		/// </summary>
		private class ResponseThread
		{
			#region Constants

			// Tcp请求头的类型
			private const int GET_FILE_SYSTEM_ENTRIES = 233;
			private const int GET_DISKS = 114514;
			private const int COPY_FILE = 7979;
			private const int CUT_FILE = 123;
			private const int PASTE_FILE = 1024;
			private const int DELETE_FILE = 321;

			#endregion

			#region Field

			private int number;      // 编号
			private int head;        // 头
			private List<byte> body; // 内容

			#endregion

			#region Property

			public int Number
			{
				get
				{
					return number;
				}
			}

			#endregion

			#region Constructor

			public ResponseThread(List<byte> request)
			{
				number = request.GetInt();
				head = request.GetInt(TCP_NUM_LENGTH);
				body = request.GetRange(TCP_NUM_LENGTH + TCP_HEAD_LENGTH,
					request.Count - TCP_NUM_LENGTH - TCP_HEAD_LENGTH);
			}

			#endregion

			/// <summary>
			/// 获取回应的内容，若无回应，则返回null
			/// </summary>
			/// <returns> 回应消息的内容 </returns>
			public List<byte> GetResponse()
			{
				switch (head)
				{
					case GET_FILE_SYSTEM_ENTRIES:
						return FileControl.GetFileSystemEntries(body);
					case GET_DISKS:
						return FileControl.GetDisks();
					case COPY_FILE:
						FileControl.CopyFilesToClipboard(body, true);
						return null;
					case CUT_FILE:
						FileControl.CopyFilesToClipboard(body, false);
						return null;
					case PASTE_FILE:
						FileControl.PasteFiles(body, SocketUtil.Instance.aoList);
						return null;
					case DELETE_FILE:
						FileControl.DeleteFiles(body);
						return null;
					default:
						return null;
				}
			}
		}
	}
}

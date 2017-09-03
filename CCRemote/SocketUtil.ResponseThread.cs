using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CCRemote
{
	public partial class SocketUtil
	{
		/// <summary>
		/// 对请求异步作出回应的类
		/// </summary>
		private class ResponseThread
		{
			#region Constants

			// Tcp请求头的类型
			private const int FILE_SYSTEM_ENTRIES = 233;

			#endregion

			#region Field

			private int number;		 // 编号
			private int head;		 // 头
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
					case FILE_SYSTEM_ENTRIES:
						return GetFileSystemEntries(body);
					default:
						return null;
				}
			}

			#region Tcp Response Method

			/// <summary>
			/// 对于目录内容的回应
			/// 回应的格式：(属性 + 路径字节数 + 路径) * N
			///			     4字节	   4字节
			/// </summary>
			/// <param name="body"></param>
			/// <returns></returns>
			private List<byte> GetFileSystemEntries(List<byte> body)
			{
				List<byte> response = new List<byte>();
				List<string> ignore = new List<string>{ "$RECYCLE.BIN", "System Volume Information"}; 

				try
				{
					string directoryPath = Encoding.UTF8.GetString(body.ToArray());
					string[] pathes = Directory.GetFileSystemEntries(directoryPath);
					foreach (var path in pathes)
					{
						int attributes = (int)File.GetAttributes(path);
						string pathWithOutDisk = path.Substring(3);
						if (!ignore.TrueForAll((str) => str != pathWithOutDisk)) // TODO 对于盘中盘下的回收站需要修改
							continue;
						byte[] pathBytes = Encoding.UTF8.GetBytes(path);

						response.AddInt(attributes);
						response.AddInt(pathBytes.Length);
						response.AddRange(pathBytes);
					}
				}
				catch { }
				return response;
			}

			#endregion
		}
	}
}

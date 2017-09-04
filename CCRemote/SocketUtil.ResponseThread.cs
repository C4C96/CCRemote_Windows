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
			private const int GET_FILE_SYSTEM_ENTRIES = 233;
			private const int GET_DISKS = 114514;

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
					case GET_FILE_SYSTEM_ENTRIES:
						return GetFileSystemEntries(body);
					case GET_DISKS:
						return GetDisks();
					default:
						return null;
				}
			}

			#region Tcp Response Method

			/// <summary>
			/// 对于目录内容的回应
			/// 请求格式：路径
			/// 回应格式：(属性 + 路径字节数 + 路径) * N
			///			   4字节    4字节
			/// </summary>
			/// <param name="body"></param>
			/// <returns> 回应 </returns>
			private List<byte> GetFileSystemEntries(List<byte> body)
			{
				List<byte> response = new List<byte>();
				FileAttributes mask = FileAttributes.Hidden | FileAttributes.System;

				try
				{
					string directoryPath = Encoding.UTF8.GetString(body.ToArray());
					string[] pathes = Directory.GetFileSystemEntries(directoryPath);
					foreach (var path in pathes)
					{
						FileAttributes attributes = File.GetAttributes(path);
						if ((attributes & mask) == mask) // 不显示系统隐藏文件
							continue;

						response.AddInt((int)attributes);
						response.AddString(path);
					}
				}
				catch { }
				return response;
			}

			/// <summary>
			/// 对于磁盘目录请求的回应
			/// 请求格式：（空）
			/// 回应格式：（路径 + 卷标字节数 + 卷标） * N
			///	       3字节(X:\)     4字节
			/// </summary>
			/// <returns></returns>
			private List<byte> GetDisks()
			{
				List<byte> response = new List<byte>();

				DriveInfo[] disks = DriveInfo.GetDrives();
				foreach(var disk in disks)
				{
					string name = disk.Name;
					string label = disk.VolumeLabel;

					response.AddRange(Encoding.UTF8.GetBytes(name.Substring(0, 3)));
					response.AddString(label);
				}
				return response;
			}

			#endregion
		}
	}
}

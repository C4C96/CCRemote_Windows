using Microsoft.VisualBasic.FileIO;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace CCRemote
{
	public partial class SocketUtil
	{
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
					case COPY_FILE:
						CopyFilesToClipboard(body, true);
						return null;
					case CUT_FILE:
						CopyFilesToClipboard(body, false);
						return null;
					case PASTE_FILE:
						PasteFiles(body);
						return null;
					case DELETE_FILE:
						DeleteFiles(body);
						return null;
					default:
						return null;
				}
			}

			#region Tcp Response Method

			#region File System

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

			/// <summary>
			/// 复制文件到剪贴板
			/// 请求格式：（路径字节数 + 路径） * N
			///	              4字节
			///	回应格式：（空）
			/// </summary>
			/// <param name="copy"> 是否是复制，否则是剪切 </param>
			private void CopyFilesToClipboard(List<byte> body, bool copy)
			{
				Thread thread = new Thread(() =>
				{
					List<string> files = body.GetStringList(out int length);
					byte[] moveEffect = { copy ? (byte)DragDropEffects.Copy : (byte)DragDropEffects.Move, 0, 0, 0 };
					MemoryStream dropEffect = new MemoryStream();
					dropEffect.Write(moveEffect, 0, moveEffect.Length);

					StringCollection fileColle = new StringCollection();
					fileColle.AddRange(files.ToArray());
					DataObject data = new DataObject("Preferred DropEffect", dropEffect);
					data.SetFileDropList(fileColle);

					Clipboard.Clear();
					Clipboard.SetDataObject(data, true);
				});
				thread.SetApartmentState(ApartmentState.STA); // 在可以调用 OLE 之前，必须将当前线程设置为单线程单元(STA)模式
				thread.IsBackground = true;
				thread.Start();
			}

			/// <summary>
			/// 从剪贴板粘贴文件到目录
			/// 请求格式：目录路径
			/// 回应格式：（空）
			/// </summary>
			/// <param name="body"></param>
			private void PasteFiles(List<byte> body)
			{
				Thread thread = new Thread(() => 
				{
					string path = Encoding.UTF8.GetString(body.ToArray());

					IDataObject data = Clipboard.GetDataObject();
					if (data.GetDataPresent(DataFormats.FileDrop))
					{
						string[] files = data.GetData(DataFormats.FileDrop) as string[];
						MemoryStream dropEffect = data.GetData("Preferred DropEffect") as MemoryStream;
						byte[] moveEffect = new byte[4];
						dropEffect.Read(moveEffect, 0, moveEffect.Length);
						foreach (var file in files)
						{
							string fileName, extension;

							#region Devide File Name

							int backslashedPos = file.LastIndexOf("\\");
							int dotPos = file.LastIndexOf(".");
							if (dotPos != -1)
							{
								fileName = file.Substring(backslashedPos + 1, dotPos - backslashedPos - 1);
								extension = file.Substring(dotPos);
							}
							else
							{
								fileName = file.Substring(backslashedPos + 1);
								extension = "";
							}

							#endregion

							#region Get uniquePath

							string uniquePath = path + fileName + extension;
							int i = 1;
							while (File.Exists(uniquePath))
								uniquePath = path + fileName + "(" + i++ + ")" + extension;

							#endregion

							if (moveEffect[0] == (byte)DragDropEffects.Copy)
								File.Copy(file, uniquePath);
							else if (moveEffect[0] == (byte)DragDropEffects.Move)
								File.Move(file, uniquePath);
						}
					}
				});
				thread.SetApartmentState(ApartmentState.STA); // 在可以调用 OLE 之前，必须将当前线程设置为单线程单元(STA)模式
				thread.IsBackground = true;
				thread.Start();
			}

			/// <summary>
			/// 将文件删除到回收站
			/// 请求格式：（路径字节数 + 路径） * N
			/// 回应格式：（空）
			/// </summary>
			/// <param name="body"></param>
			private void DeleteFiles(List<byte> body)
			{
				List<string> files = body.GetStringList(out int length);

				foreach (var file in files)
				{
					if ((File.GetAttributes(file) & FileAttributes.Directory) == FileAttributes.Directory)
						FileSystem.DeleteDirectory(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
					else
						FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				}
			}

			#endregion

			#endregion
		}
	}
}

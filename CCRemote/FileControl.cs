using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CCRemote
{
	static class FileControl
	{
		#region Constants

		// 特殊路径
		private const string DESKTOP = "%DESKTOP%";

		#endregion

		#region Public Methods

		/// <summary>
		/// 对于目录内容的回应
		/// 请求格式：路径
		/// 回应格式：当前目录字节数 + 当前目录 + (内容属性 + 内容路径字节数 + 内容路径) * N
		///			       4字节                      4字节       4字节
		/// </summary>
		/// <param name="body"></param>
		/// <returns></returns>
		public static List<byte> GetFileSystemEntries(List<byte> body)
		{
			List<byte> response = new List<byte>();
			FileAttributes mask = FileAttributes.Hidden | FileAttributes.System;

			try
			{
				string directoryPath = Encoding.UTF8.GetString(body.ToArray());

				#region Translate Special Path

				switch (directoryPath)
				{
					case DESKTOP:
						directoryPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
						break;
				}

				#endregion

				string[] pathes = Directory.GetFileSystemEntries(directoryPath);
				response.AddString(directoryPath);
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
		public static List<byte> GetDisks()
		{
			List<byte> response = new List<byte>();

			DriveInfo[] disks = DriveInfo.GetDrives();
			foreach (var disk in disks)
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
		public static void CopyFilesToClipboard(List<byte> body, bool copy)
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
		public static void PasteFiles(List<byte> body, ConcurrentBag<AsyncOperation> aoList)
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

					long size = 0;
					foreach (var file in files)
						size += GetSize(file);
					AsyncOperation ao = new AsyncOperation(string.Format("{0} {1} file{2}...", moveEffect[0] == (byte)DragDropEffects.Copy?"Copy":"Move", files.Length, files.Length==1?"":"s"), size); // TODO 文字本地化
					aoList.Add(ao);

					if (moveEffect[0] == (byte)DragDropEffects.Copy)
						foreach (var file in files)
							CopyFileSafety(file, path, true, ao);
					else if (moveEffect[0] == (byte)DragDropEffects.Move)
						foreach (var file in files)
							CopyFileSafety(file, path, false, ao);

					aoList.TryTake(out ao);
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
		public static void DeleteFiles(List<byte> body)
		{
			List<string> files = body.GetStringList(out int length);

			foreach (var file in files)
			{
				if (IsDirectory(file))
					FileSystem.DeleteDirectory(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				else
					FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
			}
		}

		#endregion

		#region Private Methods

		/// <summary>
		/// 复制/剪切文件到指定目录
		/// </summary>
		/// <param name="from"> 文件的路径 </param>
		/// <param name="to"> 目标目录 </param>
		/// <param name="copy"> true为复制，false为剪切 </param>
		private static void CopyFileSafety(string from, string to, bool copy, AsyncOperation ao)
		{
			if (IsDirectory(from))
			{
				CopyDirectory(from, to, copy, ao);
				return;
			}

			if (!Directory.Exists(to))
				Directory.CreateDirectory(to);

			string fileName, extension;

			#region Devide File Name

			int backslashedPos = from.LastIndexOf("\\");
			int dotPos = from.LastIndexOf(".");
			if (dotPos != -1)
			{
				fileName = from.Substring(backslashedPos + 1, dotPos - backslashedPos - 1);
				extension = from.Substring(dotPos);
			}
			else
			{
				fileName = from.Substring(backslashedPos + 1);
				extension = "";
			}

			#endregion
			// TODO 文件重名咋整啊
			#region Get uniquePath

			if (to[to.Length - 1] != '\\')
				to += "\\";
			string uniquePath = to + fileName + extension;
			int i = 1;
			while (File.Exists(uniquePath))
				uniquePath = to + fileName + "(" + i++ + ")" + extension;

			#endregion

			if (copy)
				File.Copy(from, uniquePath);
			else
				File.Move(from, uniquePath);

			ao.Value += new FileInfo(uniquePath).Length;
		}

		/// <summary>
		/// 复制/剪切目录到指定目录
		/// </summary>
		/// <param name="from"> 被复制/剪切的目录的路径 </param>
		/// <param name="to"> 目标目录 </param>
		/// <param name="copy"> true为复制，false为剪切 </param>
		private static void CopyDirectory(string from, string to, bool copy, AsyncOperation ao)
		{
			if (!IsDirectory(from))
			{
				CopyFileSafety(from, to, copy, ao);
				return;
			}

			int backslashedPos = from.LastIndexOf("\\");
			if (backslashedPos == -1 || backslashedPos == from.Length - 1)
				return;
			string directoryName = from.Substring(backslashedPos + 1);
			string newPath = to + "\\" + directoryName;
			if (Directory.Exists(newPath))
				Directory.CreateDirectory(newPath);
			foreach (var file in Directory.GetFiles(from))
				CopyFileSafety(file, newPath, copy, ao);
			foreach (var directory in Directory.GetDirectories(from))
				CopyDirectory(directory, newPath, copy, ao);
		}

		/// <summary>
		/// 给定的路径是否是个目录
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		private static bool IsDirectory(string path)
		{
			return ((File.GetAttributes(path) & FileAttributes.Directory) == FileAttributes.Directory);
		}

		/// <summary>
		/// 获取文件/目录的大小（字节）
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		private static long GetSize(string path)
		{
			if (!IsDirectory(path))
				return new FileInfo(path).Length;

			long size = 0;
			foreach (var file in Directory.GetFiles(path))
				size += new FileInfo(file).Length;
			foreach (var directory in Directory.GetDirectories(path))
				size += GetSize(directory);

			return size;
		}

		#endregion
	}
}

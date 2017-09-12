using System;
using System.Collections.Generic;
using System.Text;

namespace CCRemote
{
	/// <summary>
	/// IList<byte>的拓展方法
	/// </summary>
	static class ByteListEx
	{
		/// <summary>
		/// 往列表中增加一个int，作为四个字节
		/// </summary>
		/// <param name="list"></param>
		/// <param name="integer"></param>
		public static void AddInt(this IList<byte> list, int integer)
		{
			list.Add((byte)(integer >> 24));
			list.Add((byte)(integer >> 16));
			list.Add((byte)(integer >> 8));
			list.Add((byte)(integer));
		}

		/// <summary>
		/// 从队列中取4个字节作为整型，不改变原列表
		/// </summary>
		/// <param name="list"></param>
		/// <param name="index">起始字节</param>
		/// <exception cref="IndexOutOfRangeException" />
		/// <returns></returns>
		public static int GetInt(this IList<byte> list, int index = 0)
		{
			if (index < 0 ||
				index + 4 > list.Count)
				throw new IndexOutOfRangeException();
			return (list[index] << 24)
				+ (list[index + 1] << 16)
				+ (list[index + 2] << 8)
				+ list[index + 3];
		}

		/// <summary>
		/// 将一个字符串按长度(4字节)+bytes的格式加入队列(UTF-8编码)
		/// </summary>
		/// <param name="list"></param>
		/// <param name="str"></param>
		public static void AddString(this List<byte> list, string str)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(str);
			list.AddInt(bytes.Length);
			list.AddRange(bytes);
		}

		/// <summary>
		/// 从队列中获取一个字符串
		/// </summary>
		/// <param name="list"></param>
		/// <param name="byteLength"> 队列中被读取的字节数，失败则返回0 </param>
		/// <param name="index"> 起始位置 </param>
		/// <returns> 获得的字符串，若失败则返回null </returns>
		public static string GetString(this List<byte> list, out int byteLength, int index = 0)
		{
			if (index + 4 > list.Count)
			{
				byteLength = 0;
				return null;
			}
			int strLength = list.GetInt(index);
			index += 4;
			if (index + strLength > list.Count)
			{
				byteLength = 0;
				return null;
			}
			List<byte> bytes = list.GetRange(index, strLength);
			byteLength = strLength + 4;
			return Encoding.UTF8.GetString(bytes.ToArray());
		}

		/// <summary>
		/// 从队列中获取一组字符串
		/// </summary>
		/// <param name="list"></param>
		/// <param name="byteLength"> 队列中被读取的字节数，失败则返回0 </param>
		/// <param name="index"> 起始位置 </param>
		/// <returns> 获得的字符串，若失败则返回空集合 </returns>
		public static List<string> GetStringList(this List<byte> list, out int byteLength, int index = 0)
		{
			string str;
			int cursor = index;
			List<string> ret = new List<string>();
			while ((str = list.GetString(out int length, cursor)) != null)
			{
				ret.Add(str);
				cursor += length;
			}
			byteLength = cursor - index;
			return ret;
		}

		public static void AddDouble(this List<byte> list, double d)
		{
			list.AddRange(BitConverter.GetBytes(d));
		}

		public static void AddAsyncOperation(this List<byte> list, AsyncOperation asyncOperation)
		{
			list.AddInt(asyncOperation.Id);
			list.AddString(asyncOperation.Name);
			list.AddDouble(asyncOperation.MaxValue);
			list.AddDouble(asyncOperation.Value);
		}
	}
}

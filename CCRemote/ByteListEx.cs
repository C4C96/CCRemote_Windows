using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
			return list[index] << 24
				+ list[index + 1] << 16
				+ list[index + 2] << 8
				+ list[index + 3];
		}
	}
}

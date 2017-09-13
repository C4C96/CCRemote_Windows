using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCRemote
{
	/// <summary>
	/// 描述一个异步操作进度的类
	/// </summary>
	class AsyncOperation 
	{
		#region Fields

		private static int count = 0;
		private readonly int id;
		private readonly string name;
		private readonly long maxValue;

		#endregion

		#region Properties

		public int Id
		{
			get
			{
				return id;
			}
		}

		public string Name
		{
			get
			{
				return name;
			}
		}

		public long MaxValue
		{
			get
			{
				return maxValue;
			}
		}

		public long Value
		{ get; set; }

		#endregion

		public AsyncOperation(string name, long maxValue)
		{
			this.name = name;
			this.maxValue = maxValue;
			this.id = count++;
		}
	}
}

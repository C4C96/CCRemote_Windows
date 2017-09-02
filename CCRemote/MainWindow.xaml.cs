using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.ComponentModel;
using System.Threading;
using System.Collections;

namespace CCRemote
{
	/// <summary>
	/// MainWindow.xaml 的交互逻辑
	/// </summary>
	public partial class MainWindow : Window
	{
		#region Fields

		private NotifyIcon notifyIcon;  //通知栏图标
		
		#endregion

		#region Properties

		public int Port
		{
			get;
			private set;
		}

		#endregion

		public MainWindow()
		{
			InitializeComponent();

			#region Initialize notifyIcon

			notifyIcon = new NotifyIcon()
			{
				//取应用图标作为通知栏图标
				Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath),
				Text = "Text",
				Visible = true,
				ContextMenu = new System.Windows.Forms.ContextMenu(new System.Windows.Forms.MenuItem[]
				{
					new System.Windows.Forms.MenuItem("Show", (o, e)=>Show()),
					new System.Windows.Forms.MenuItem("Exit", (o, e)=>Environment.Exit(0)),
				}),
			};
			notifyIcon.MouseDoubleClick += (o, e) => 
			{
				if (e.Button == MouseButtons.Left)
					if (Visibility == Visibility.Visible)
						Activate();
					else
						Show();
			};

			#endregion

			Port = 1234; // TODO: 这玩意是临时的

			SocketUtil socketUtil = new SocketUtil(Port);
			new Thread(socketUtil.UdpListener) { IsBackground = true }.Start();
			new Thread(socketUtil.TcpListener) { IsBackground = true }.Start();
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			e.Cancel = true;
			Hide();
			notifyIcon.BalloonTipText = "Still working";
			notifyIcon.ShowBalloonTip(1500);
			base.OnClosing(e);
		}
	}
}

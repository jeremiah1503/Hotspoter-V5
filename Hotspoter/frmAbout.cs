using System;
using System.Diagnostics;
using System.Windows.Forms;
using HotspotShare.Classes;

namespace HotspotShare
{
	partial class frmAbout : frmBase
	{
		public frmAbout()
		{
			InitializeComponent();
		}

		private void btnClose_Click(object sender, EventArgs e)
		{
			Close();
		}

		




        private void Button1_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var start = new ProcessStartInfo("http://hotspotteam.tk");
            try
            {
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch { }
        }

        private void LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var start = new ProcessStartInfo("http://hotspotteam.tk");
            try
            {
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch { }
        }
	}
}

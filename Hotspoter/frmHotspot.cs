using Blue.Windows;
using HotspotShare.Api;
using HotspotShare.Classes;
using HotspotShare.HostedNetwork;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Net;
using Microsoft.VisualBasic;
using System.Management;



namespace HotspotShare
{
    public partial class frmHotspot : frmBase
    {
        #region [Private Fields]

        private bool _hasStationUsers = false;
        private bool _hasAnyUserShouldReadStatus = false;
        int OrigTime = 1800;
        private bool _minimizedWarning = false;
        private bool _isVisibleCore = false;
        private bool _manualStartIsInPrgress = false;
        private StickyWindow _sticky;

        private HostedNetworkManager hostedNetworkManager;
        private string _icsDomainNameSuffix;

        int h, m, s;
        #endregion

        public frmHotspot()
        {
            ApplyLanguage();

            InitializeComponent();
            _sticky = new StickyWindow(this)
            {
                StickToScreen = true
            };
            RememberWindow.MoveToEdges_BottomRight(this);

            _isVisibleCore = true;
            if (AppConfig.AppStartedFromStartup)
                _isVisibleCore = false;

            InitializeForm();


#if TRACE
            LogExceptions.ClearTrace();
#endif

        }
        protected override void SetVisibleCore(bool value)
        {
            // Preventing the form to display in windows auto-start by ignoring the *value*
            base.SetVisibleCore(_isVisibleCore);
        }

        #region [General Private Methods]

        void InitializeForm()
        {
            lstUsers.Items.Clear();

            //SetAppStatus();

            hostedNetworkManager = new HostedNetworkManager();
            hostedNetworkManager.ThreadedEvents = new UiThreadedEvents(this);
            hostedNetworkManager.OnConnectionsListChanged += HostedNetworkManagerOnConnectionsListChanged;
            hostedNetworkManager.OnSharedConnectionChanged += HostedNetworkManagerOnSharedConnectionChanged;
            hostedNetworkManager.OnWorkingStatusChanged += HostedNetworkManagerOnWorkingStatusChanged;
            hostedNetworkManager.OnUserUpdated += HostedNetworkManagerOnUserUpdated;
            hostedNetworkManager.OnUserConnected += HostedNetworkManagerOnUserConnected;
            hostedNetworkManager.OnUserLeave += HostedNetworkManagerOnUserLeave;
            hostedNetworkManager.OnFailedToEnableSharing += HostedNetworkManagerOnFailedToEnableSharing;

            // reading ICS connections list
            hostedNetworkManager.ReadNetworkConnectionsAsync();

            ReadFormSettings();

            _icsDomainNameSuffix = SystemTweak.IcsDomainSuffix();

            if (AppConfig.AppStartedFromStartup)
            {
                if (ValidateForm(false))
                {
                    SaveFormSettings();
                    hostedNetworkManager.StartAsync();
                }
            }
        }

        void ApplyLanguage()
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(AppConfig.Instance.UiLanguage);
            Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture;
        }

        void SaveFormSettings()
        {
            AppConfig.Instance.AutoDetectInternet = chkAutoDetectInternet.Checked;
            AppConfig.Instance.NotifyNewUser = chkUsersNotifyNewUser.Checked;
            AppConfig.Instance.NotifyUserConnecting = chkUsersNotifyUserConnecting.Checked;
            AppConfig.Instance.Password = txtPass.Text;
            AppConfig.Instance.NetworkSsid = txtSSID.Text;
            AppConfig.Instance.ConnectionShare = cmdSharedConnection.Text;
            AppConfig.Instance.AutoStartWithWindows = chkAutoStartWindows.Checked;

            var item = cmdSharedConnection.SelectedItem as IcsConnection;
            if (item == null)
            {
                AppConfig.Instance.InternetNetwork = null;
            }
            else
            {
                AppConfig.Instance.InternetNetwork = item.Guid;
            }


            AppConfig.Instance.SaveConfig();

            hostedNetworkManager.ConfigAutoInternet = AppConfig.Instance.AutoDetectInternet;
            hostedNetworkManager.ConfigInternetNetwork = AppConfig.Instance.InternetNetwork;
            hostedNetworkManager.ConfigPassword = AppConfig.Instance.Password;
            hostedNetworkManager.ConfigShareInternet = true;
            hostedNetworkManager.ConfigSsid = AppConfig.Instance.NetworkSsid;
        }

        void ReadFormSettings()
        {
            chkAutoDetectInternet.Checked = AppConfig.Instance.AutoDetectInternet;
            chkUsersNotifyNewUser.Checked = AppConfig.Instance.NotifyNewUser;
            chkUsersNotifyUserConnecting.Checked = AppConfig.Instance.NotifyUserConnecting;
            txtPass.Text = AppConfig.Instance.Password;
            txtSSID.Text = AppConfig.Instance.NetworkSsid;
            if (!string.IsNullOrEmpty(AppConfig.Instance.ConnectionShare))
                cmdSharedConnection.Text = AppConfig.Instance.ConnectionShare;
            chkAutoStartWindows.Checked = AppConfig.Instance.AutoStartWithWindows;


            hostedNetworkManager.ConfigAutoInternet = AppConfig.Instance.AutoDetectInternet;
            hostedNetworkManager.ConfigInternetNetwork = AppConfig.Instance.InternetNetwork;
            hostedNetworkManager.ConfigPassword = AppConfig.Instance.Password;
            hostedNetworkManager.ConfigShareInternet = true;
            hostedNetworkManager.ConfigSsid = AppConfig.Instance.NetworkSsid;
        }

        bool ValidateForm(bool showError = true)
        {
            string msg = "";

            txtSSID.Text = txtSSID.Text.Trim();
            txtPass.Text = txtPass.Text.Trim();

            if (txtSSID.Text.Length <= 0)
            {
                msg += Hotspoter.Language.Form_SSIDRequired + "\n";
            }

            if (txtSSID.Text.Length > 32)
            {
                msg += Hotspoter.Language.Form_SSIDLong + "\n";
            }

            if (txtPass.Text.Length == 0)
            {
                msg += Hotspoter.Language.Form_PassRequired + "\n";
            }
            else if (txtPass.Text.Length < 8)
            {
                msg += Hotspoter.Language.Form_PassShort + "\n";
            }

            if (txtPass.Text.Length > 64)
            {
                msg += Hotspoter.Language.Form_PassLong + "\n";
            }

            //if (cmbToShareConnection.SelectedIndex < 0)
            //{
            //	msg += Hotspoter.Language.Form_ConnectionSelected + "\n";
            //}

            if (msg.Length > 0)
            {
                if (showError)
                    MessageBox.Show(msg, Hotspoter.Language.Form_InvalidInput, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, AppConfig.Instance.MessageBoxOptions);
                return false;
            }
            return true;
        }
        /// <summary>
        /// Updating cmdSharedConnection
        /// </summary>
        void UiUpdateConnectionComboBox(HostedNetworkManager hostednetwork)
        {
            var connections = hostednetwork.IcsConnectedConnections;
            var shared = hostednetwork.SharedConnection;

            cmdSharedConnection.DisplayMember = "Name";
            cmdSharedConnection.ValueMember = "Guid";
            cmdSharedConnection.DataSource = null;
            cmdSharedConnection.Items.Clear();
            cmdSharedConnection.DataSource = connections;

            if (shared == null)
            {
                var savedConnName = AppConfig.Instance.ConnectionShare;
                if (string.IsNullOrWhiteSpace(savedConnName))
                {
                    cmdSharedConnection.SelectedIndex = 0;
                }
                else
                {
                    cmdSharedConnection.Text = savedConnName;
                }
            }
            else
            {
                cmdSharedConnection.SelectedItem = shared;

                var newName = cmdSharedConnection.Text;
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    AppConfig.Instance.ConnectionShare = newName;
                }
            }
        }

        void UiUpdateFormWorkingStatus(HostedNetworkManager hostednetwork)
        {
            bool isBusy = false;
            bool isStarted = false;
            switch (hostednetwork.Status)
            {
                case HostedNetworkManager.WorkingStatus.Started:
                    tabMain.SelectTab(tabUsers);
                    lblStatus.Text = Hotspoter.Language.Form_StartedStated;
                    lblStatus.ForeColor = Color.Green;
                    btnStartStop.Text = Hotspoter.Language.Form_BtnStoped;
                    pictureBox11.Image = Hotspoter.Properties.Resources.ok;
                    sysIcon.Text = "Hotspoter : Hotspot Is Running";
                    isBusy = false;
                    isStarted = true;
                    _manualStartIsInPrgress = false;

                    sysIcon.Icon = Hotspoter.Properties.Resources.AppiconTray;
                    break;

                case HostedNetworkManager.WorkingStatus.Stopped:
                    tabMain.SelectTab(tabHotspot);
                    lblStatus.Text = Hotspoter.Language.Form_StopedStated;
                    lblStatus.ForeColor = Color.Red;
                    btnStartStop.Text = Hotspoter.Language.Form_BtnStarted;
                    sysIcon.Text = "Hotspoter : Hotspot Is Stopped !";
                    isBusy = false;
                    pictureBox11.Image = Hotspoter.Properties.Resources.oh;
                    isStarted = false;
                    //_manualStartIsInPrgress = false;

                    // reload the connections list
                    hostedNetworkManager.ReadNetworkConnectionsAsync();

                    sysIcon.Icon = Hotspoter.Properties.Resources.AppiconTrayDisabled;
                    break;


                case HostedNetworkManager.WorkingStatus.Starting:
                    lblStatus.Text = "Starting...";
                    lblStatus.ForeColor = Color.DarkRed;
                    btnStartStop.Text = "Starting...";
                    sysIcon.Text = "Hotspot Share (Starting)";
                    isBusy = true;
                    isStarted = false;
                    break;

                case HostedNetworkManager.WorkingStatus.Stopping:
                    lblStatus.Text = "Stopping...";
                    lblStatus.ForeColor = Color.DarkRed;
                    btnStartStop.Text = "Stopping...";
                    sysIcon.Text = "Hotspot Share (Stopping)";
                    isBusy = true;
                    isStarted = false;
                    //_manualStartIsInPrgress = false;
                    break;

                case HostedNetworkManager.WorkingStatus.StartFailed:
                    tabMain.SelectTab(tabHotspot);
                    lblStatus.Text = "Startup Failed";
                    lblStatus.ForeColor = Color.Red;
                    btnStartStop.Text = "Try Start";
                    sysIcon.Text = "Hotspot Share (Start Failed)";
                    isBusy = false;
                    isStarted = false;
                    _manualStartIsInPrgress = false;

                    // reload the connections list
                    hostedNetworkManager.ReadNetworkConnectionsAsync();
                    break;

                case HostedNetworkManager.WorkingStatus.StopFailed:
                    tabMain.SelectTab(tabHotspot);
                    lblStatus.Text = "Stopping Failed";
                    lblStatus.ForeColor = Color.Red;
                    btnStartStop.Text = "Try Stop";
                    sysIcon.Text = "Hotspot Share (Stop Failed)";
                    isBusy = false;
                    isStarted = true;
                    _manualStartIsInPrgress = false;
                    break;
            }

            var enableControls = !(isBusy || isStarted);
            gpbSettings.Enabled = enableControls;
            gpbInternet.Enabled = enableControls;

            btnStartStop.Enabled = !isBusy;
        }


        void UiUpdateStationsList(HostedNetworkManager hostednetwork, IList<StationUser> newUsers = null)
        {
            bool isFarsi = AppConfig.Instance.IsFarsi;

            var desc = new List<string>();
            lstUsers.Items.Clear();

            var hasUsers = hostednetwork.StationUsers.Count > 0;
            _hasAnyUserShouldReadStatus = false;

            var usersToNotify = new List<StationUser>();

            foreach (var user in hostednetwork.StationUsers)
            {
                if (user.Status == StationUser.UserStatus.Connecting ||
                    user.HostNameResolved == false)
                    // this user information is incomplete
                    _hasAnyUserShouldReadStatus = true;

                if (user.HostNameResolved && !user.NotifiedConnected)
                {
                    usersToNotify.Add(user);
                    user.NotifiedConnected = true;
                }
                user.SetIcsDomainSuffix(_icsDomainNameSuffix);

                var listViewItem = new ListViewItem(user.HostNameNoPrefix, 0);
                listViewItem.Text = user.HostNameNoPrefix;

                if (!string.IsNullOrEmpty(user.Vendor))
                    desc.Add(user.Vendor);

                if (!string.IsNullOrEmpty(user.IpAddress))
                    desc.Add(user.IpAddress);

                if (!string.IsNullOrEmpty(user.MacAddress))
                    desc.Add(user.MacAddress);

                desc.Add(user.JoinDate.ToString("yyyy/MM/dd HH:mm"));

                if (isFarsi)
                    desc.Reverse();

                var descText = Common.JoinList(", ", desc);
                if (isFarsi)
                    descText = "\u200E" + descText;

                listViewItem.SubItems.Add(descText);
                listViewItem.ToolTipText = user.HostName + "\r\n" + descText;
                listViewItem.Tag = user;

                desc.Clear();

                lstUsers.Items.Add(listViewItem);
            }
            if (_hasStationUsers != hasUsers)
            {
                if (hasUsers)
                {
                    sysIcon.Icon = Hotspoter.Properties.Resources.AppiconTrayUser;
                    Icon = Hotspoter.Properties.Resources.AppiconTrayUser;
                }
                else
                {
                    Icon = Hotspoter.Properties.Resources.AppiconTray;

                    if (hostednetwork.Status == HostedNetworkManager.WorkingStatus.Stopped)
                    {
                        sysIcon.Icon = Hotspoter.Properties.Resources.AppiconTrayDisabled;
                    }
                    else
                    {
                        sysIcon.Icon = Hotspoter.Properties.Resources.AppiconTray;
                    }
                }
                _hasStationUsers = hasUsers;
            }
            if (usersToNotify.Count > 0 && chkUsersNotifyNewUser.Checked)
            {
                var message = string.Join("\n", usersToNotify.Select(a => a.HostNameNoPrefix + ", " + a.IpAddress.ToString()));

                message = "The following user(s) are connected:\n" + message;
                sysIcon.ShowBalloonTip(1000, Hotspoter.Language.App_Name, message, ToolTipIcon.Info);
            }
            else if (newUsers != null && chkUsersNotifyUserConnecting.Checked)
            {
                sysIcon.ShowBalloonTip(1000, Hotspoter.Language.App_Name, Hotspoter.Language.Balloon_NewUser, ToolTipIcon.Info);
            }

            SetRefreshStationsTimer();
        }

        void SetRefreshStationsTimer()
        {
            if (_hasAnyUserShouldReadStatus)
            {
                tmrStationsList.Enabled = true;
            }
            else
            {
                tmrStationsList.Enabled = false;
            }
        }

        void UiShow()
        {
            _isVisibleCore = true;
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            this.Focus();
        }


        private void UiShowNoInternetAccess(HostedNetworkManager hostednetwork, Exception exception)
        {
            // only if user is requested start manually
            //if (!_manualStartIsInPrgress)
            //	return;
            var result = MessageBox.Show(
                "Hotspot is failed to enable internet sharing automatically after many tries.\n" +
                "You have to enable the internet sharing manually.\n" +
                "Do you want to see how?",
                "Failed to enable windows sharing",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1,
                AppConfig.Instance.MessageBoxOptions);
            if (result == DialogResult.OK)
            {
                try
                {
                    var filePath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), AppConfig.HelpNoInternetAccess);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception)
                {
                }
            }
        }

        #endregion

        #region [Hosted Network Events]
        private void HostedNetworkManagerOnUserLeave(HostedNetworkManager hostednetwork)
        {
            UiUpdateStationsList(hostednetwork);
        }

        private void HostedNetworkManagerOnUserConnected(HostedNetworkManager hostednetwork, IList<StationUser> newUsers)
        {
            UiUpdateStationsList(hostednetwork, newUsers);
        }

        private void HostedNetworkManagerOnUserUpdated(HostedNetworkManager hostednetwork)
        {
            UiUpdateStationsList(hostednetwork);
        }

        private void HostedNetworkManagerOnWorkingStatusChanged(HostedNetworkManager hostednetwork)
        {
            UiUpdateFormWorkingStatus(hostednetwork);
        }

        private void HostedNetworkManagerOnSharedConnectionChanged(HostedNetworkManager hostednetwork)
        {
            UiUpdateConnectionComboBox(hostednetwork);
        }

        private void HostedNetworkManagerOnConnectionsListChanged(HostedNetworkManager hostednetwork)
        {
            UiUpdateConnectionComboBox(hostednetwork);
        }

        private void HostedNetworkManagerOnFailedToEnableSharing(HostedNetworkManager hostednetwork, Exception exception)
        {
            UiShowNoInternetAccess(hostednetwork, exception);
        }
        #endregion

        #region [Form Events]
        private void cmdSharedConnection_SelectedIndexChanged(object sender, EventArgs e)
        {
        }

        private void frmHotspot_Load(object sender, EventArgs e)
        {
            if ((System.IO.File.Exists("header.jpg") == true))
            {
                makersToolStripMenuItem1.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                mnuSys.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                     mnuShow.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                aboutToolStripMenuItem1.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                panel1.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");

                btnStartStop.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                MenuStrip1.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                btnRefresh.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                metroButton1.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
                metroButton2.BackgroundImage = Image.FromFile((Application.StartupPath) + @"\header.jpg");
            }
            Process.Start("updt.exe");


        }

        private void frmHotspot_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveFormSettings();
            if (e.CloseReason == CloseReason.UserClosing)
            {
                _isVisibleCore = false;
                this.Hide();

                sysIcon.Visible = true;
                if (!_minimizedWarning)
                {
                    _minimizedWarning = true;
                    sysIcon.ShowBalloonTip(1000, Hotspoter.Language.App_Name, Hotspoter.Language.Balloon_Minimized, ToolTipIcon.Info);
                }
                e.Cancel = true;
            }
            else
            {
                hostedNetworkManager.StopSynced();
            }
        }

        private void frmHotspot_FormClosed(object sender, FormClosedEventArgs e)
        {
            SetRefreshStationsTimer();
        }

        private void frmHotspot_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                hostedNetworkManager.ReadStationUsersAsync();
                SetRefreshStationsTimer();
            }
        }

        private void btnStartStop_Click(object sender, EventArgs e)
        {
            SaveFormSettings();

            switch (hostedNetworkManager.Status)
            {
                case HostedNetworkManager.WorkingStatus.Stopping:
                case HostedNetworkManager.WorkingStatus.Stopped:
                case HostedNetworkManager.WorkingStatus.StartFailed:
                    _manualStartIsInPrgress = true;
                    hostedNetworkManager.ResetFailedToEnableSharingNetwork();

                    hostedNetworkManager.StartAsync();
                    break;

                case HostedNetworkManager.WorkingStatus.Started:
                case HostedNetworkManager.WorkingStatus.Starting:
                case HostedNetworkManager.WorkingStatus.StopFailed:
                    _manualStartIsInPrgress = false;
                    hostedNetworkManager.StopAsync();
                    break;
                default:
                    // Do nothing

                    break;
            }

            if (lblStatus.Text == ("Stopped"))
            {
                pictureBox11.Image = Hotspoter.Properties.Resources.ok;
            }
            if (lblStatus.Text == ("Started"))
            {
                pictureBox11.Image = Hotspoter.Properties.Resources.oh;
            }


        }

        private void chkAutoDetectInternet_CheckedChanged(object sender, EventArgs e)
        {
            cmdSharedConnection.Enabled = !chkAutoDetectInternet.Checked;
            if (cmdSharedConnection.Enabled)
            {
                hostedNetworkManager.ReadNetworkConnectionsAsync();
            }
        }
        private void txtPass_Enter(object sender, EventArgs e)
        {
            txtPass.PasswordChar = '\0';
        }

        private void txtPass_Leave(object sender, EventArgs e)
        {
            txtPass.PasswordChar = '●';
        }

        private void mnuShow_Click(object sender, EventArgs e)
        {
            UiShow();
        }

        private void mnuExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void StartupAppInRegistery(bool enable)
        {
            if (enable)
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Application.ProductName,
                    string.Format("\"{0}\" " + AppConfig.AutoStartupKey, Application.ExecutablePath));

            }
            else
            {
                Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Application.ProductName, "Hotspoter");
            }
        }

        private void chkAutoStartWindows_CheckedChanged(object sender, EventArgs e)
        {
            var auto = chkAutoStartWindows.Checked;
            try
            {
                var proc = new Process();
                proc.EnableRaisingEvents = true;
                proc.Exited += (s, args) =>
                {
                    if (proc.ExitCode == 0)
                    {
                        StartupAppInRegistery(false);
                    }
                    else
                    {
                        StartupAppInRegistery(true);
                    }
                    proc.Dispose();
                };


                if (auto)
                {
                    proc.StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments =
                            string.Format("/create /f /sc onlogon /tn Hotspoter /rl highest /DELAY 0001:00 /tr \"{0} -startup\"",
                                Application.ExecutablePath),
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                }
                else
                {
                    proc.StartInfo = (new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = "/delete /f /tn Hotspoter",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    });
                }
                proc.Start();

            }
            catch { }
        }



        private void btnRefresh_Click(object sender, EventArgs e)
        {
            hostedNetworkManager.ReadStationUsersAsync();
        }

        private void sysIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                UiShow();
            }
        }

        private void tmrStationsList_Tick(object sender, EventArgs e)
        {
            hostedNetworkManager.ReadStationUsersAsync();
            SetRefreshStationsTimer();
        }

        private void mnuCopyUserIP_Click(object sender, EventArgs e)
        {
            if (lstUsers.SelectedItems == null || lstUsers.SelectedItems.Count == 0)
                return;
            var user = lstUsers.SelectedItems[0].Tag as StationUser;
            if (user == null)
                return;
            Clipboard.SetText(user.IpAddress ?? "");
        }

        private void mnuCopyUserMACAddress_Click(object sender, EventArgs e)
        {
            if (lstUsers.SelectedItems == null || lstUsers.SelectedItems.Count == 0)
                return;
            var user = lstUsers.SelectedItems[0].Tag as StationUser;
            if (user == null)
                return;
            Clipboard.SetText(user.MacAddress ?? "");
        }

        private void mnuCopyUserHostname_Click(object sender, EventArgs e)
        {
            if (lstUsers.SelectedItems == null || lstUsers.SelectedItems.Count == 0)
                return;
            var user = lstUsers.SelectedItems[0].Tag as StationUser;
            if (user == null)
                return;
            Clipboard.SetText(user.HostName ?? "");
        }

        private void mnuCopyUserInfo_Click(object sender, EventArgs e)
        {
            if (lstUsers.SelectedItems == null || lstUsers.SelectedItems.Count == 0)
                return;
            if (lstUsers.SelectedItems == null || lstUsers.SelectedItems.Count == 0)
                return;
            var user = lstUsers.SelectedItems[0];
            if (user == null)
                return;
            Clipboard.SetText(user.ToolTipText ?? "");
        }
        #endregion

        private void mnuUsers_Opening(object sender, CancelEventArgs e)
        {
            if (lstUsers.SelectedItems == null || lstUsers.SelectedItems.Count == 0)
            {
                e.Cancel = true;
            }

        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frm = new frmAbout())
            {
                frm.ShowDialog();
            }
        }

        private void DonateToThisProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var start = new ProcessStartInfo("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=FPJVTMZGN2SVS");
            try
            {
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch { }
        }

        private void timer1_Tick(object sender, EventArgs e)

        {

            bool bb = CheckForInternetConnection();

            if (bb == true)
            {
                label12.Text = "Available";
                pictureBox6.Image = Hotspoter.Properties.Resources.ok;
            }
            if (bb == false)
            {
                label12.Text = "Not Available";
                pictureBox6.Image = Hotspoter.Properties.Resources.oh;
            }

            timer1.Enabled = false;
        }
        public static bool CheckForInternetConnection()
        {
            try
            {
                using (var client = new WebClient())
                {
                    using (var stream = client.OpenRead("http://www.google.com"))
                    {

                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private void chkAutoStartWindows_CheckedChanged_1(object sender, EventArgs e)
        {

        }

        public static string GetOSFriendlyName()
        {
            string result = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
            foreach (ManagementObject os in searcher.Get())
            {
                result = os["Caption"].ToString();
                break;
            }
            return result;
        }

        private void NetworkConnectionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/c ncpa.cpl";
            process.StartInfo = startInfo;
            process.Start();
        }

        private void makersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("maker.exe");


        }

        private void CheckForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "Update - Hotspoter.exe";
            Process.Start(startInfo);
            
        }

        private void btnRefresh_Click_1(object sender, EventArgs e)
        {
            hostedNetworkManager.ReadStationUsersAsync();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            h = (int)numericUpDown1.Value;
            m = (int)numericUpDown2.Value;
            s = (int)numericUpDown3.Value;



        }

        private void btnStartStop_Click_1(object sender, EventArgs e)
        {
            SaveFormSettings();
            switch (hostedNetworkManager.Status)
            {
                case HostedNetworkManager.WorkingStatus.Stopping:
                case HostedNetworkManager.WorkingStatus.Stopped:
                case HostedNetworkManager.WorkingStatus.StartFailed:
                    _manualStartIsInPrgress = true;
                    hostedNetworkManager.ResetFailedToEnableSharingNetwork();

                    pictureBox11.Image = Hotspoter.Properties.Resources.ok;
                    hostedNetworkManager.StartAsync();
                    break;

                case HostedNetworkManager.WorkingStatus.Started:
                case HostedNetworkManager.WorkingStatus.Starting:
                case HostedNetworkManager.WorkingStatus.StopFailed:
                    _manualStartIsInPrgress = false;

                    pictureBox11.Image = Hotspoter.Properties.Resources.oh;
                    hostedNetworkManager.StopAsync();
                    break;
                default:
                    // Do nothing
                    break;
            }
        }

        private void chkAutoStartWindows_CheckedChanged_2(object sender, EventArgs e)
        {

        }

        private void aboutToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using (var frm = new frmAbout())
            {
                frm.ShowDialog();
            }
        }

        private void metroCheckBox1_CheckedChanged(object sender, EventArgs e)
        {
            cmdSharedConnection.Enabled = !chkAutoDetectInternet.Checked;
            if (cmdSharedConnection.Enabled)
            {
                hostedNetworkManager.ReadNetworkConnectionsAsync();
            }
        }

        private void metroCheckBox1_CheckedChanged_1(object sender, EventArgs e)
        {
            var auto = chkAutoStartWindows.Checked;
            try
            {
                var proc = new Process();
                proc.EnableRaisingEvents = true;
                proc.Exited += (s, args) =>
                {
                    if (proc.ExitCode == 0)
                    {
                        StartupAppInRegistery(false);
                    }
                    else
                    {
                        StartupAppInRegistery(true);
                    }
                    proc.Dispose();
                };


                if (auto)
                {
                    proc.StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments =
                            string.Format("/create /f /sc onlogon /tn Hotspoter /rl highest /DELAY 0001:00 /tr \"{0} -startup\"",
                                Application.ExecutablePath),
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                }
                else
                {
                    proc.StartInfo = (new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = "/delete /f /tn Hotspoter",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    });
                }
                proc.Start();

            }
            catch { }
        }

        private void contactUsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("Contact.exe");
        }

        private void refreshstat(object sender, EventArgs e)
        {
            pictureBox17.Image = Hotspoter.Properties.Resources.load;
            bool isBusy = false;
            bool isStarted = false;
            timer1.Enabled = true;
            switch (hostedNetworkManager.Status)
            {
                case HostedNetworkManager.WorkingStatus.Started:
                    tabMain.SelectTab(tabUsers);
                    lblStatus.Text = Hotspoter.Language.Form_StartedStated;
                    lblStatus.ForeColor = Color.Green;
                    btnStartStop.Text = Hotspoter.Language.Form_BtnStoped;
                    pictureBox11.Image = Hotspoter.Properties.Resources.ok;
                    sysIcon.Text = "Hotspoter : Hotspot Is Running";
                    isBusy = false;
                    isStarted = true;
                    _manualStartIsInPrgress = false;

                    sysIcon.Icon = Hotspoter.Properties.Resources.AppiconTray;
                    break;

                case HostedNetworkManager.WorkingStatus.Stopped:
                    tabMain.SelectTab(tabHotspot);
                    lblStatus.Text = Hotspoter.Language.Form_StopedStated;
                    lblStatus.ForeColor = Color.Red;
                    btnStartStop.Text = Hotspoter.Language.Form_BtnStarted;
                    sysIcon.Text = "Hotspoter : Hotspot Is Stopped !";
                    isBusy = false;
                    pictureBox11.Image = Hotspoter.Properties.Resources.oh;
                    isStarted = false;
                    //_manualStartIsInPrgress = false;

                    // reload the connections list
                    hostedNetworkManager.ReadNetworkConnectionsAsync();

                    sysIcon.Icon = Hotspoter.Properties.Resources.AppiconTrayDisabled;
                    break;


                case HostedNetworkManager.WorkingStatus.Starting:
                    lblStatus.Text = "Starting...";
                    lblStatus.ForeColor = Color.DarkRed;
                    btnStartStop.Text = "Starting...";
                    sysIcon.Text = "Hotspot Share (Starting)";
                    isBusy = true;
                    isStarted = false;
                    break;

                case HostedNetworkManager.WorkingStatus.Stopping:
                    lblStatus.Text = "Stopping...";
                    lblStatus.ForeColor = Color.DarkRed;
                    btnStartStop.Text = "Stopping...";
                    sysIcon.Text = "Hotspot Share (Stopping)";
                    isBusy = true;
                    isStarted = false;
                    //_manualStartIsInPrgress = false;
                    break;

                case HostedNetworkManager.WorkingStatus.StartFailed:
                    tabMain.SelectTab(tabHotspot);
                    lblStatus.Text = "Startup Failed";
                    lblStatus.ForeColor = Color.Red;
                    btnStartStop.Text = "Try Start";
                    sysIcon.Text = "Hotspot Share (Start Failed)";
                    isBusy = false;
                    isStarted = false;
                    _manualStartIsInPrgress = false;

                    // reload the connections list
                    hostedNetworkManager.ReadNetworkConnectionsAsync();
                    break;

                case HostedNetworkManager.WorkingStatus.StopFailed:
                    tabMain.SelectTab(tabHotspot);
                    lblStatus.Text = "Stopping Failed";
                    lblStatus.ForeColor = Color.Red;
                    btnStartStop.Text = "Try Stop";
                    sysIcon.Text = "Hotspot Share (Stop Failed)";
                    isBusy = false;
                    isStarted = true;
                    _manualStartIsInPrgress = false;
                    break;
            }

            var enableControls = !(isBusy || isStarted);
            gpbSettings.Enabled = enableControls;
            gpbInternet.Enabled = enableControls;
            pictureBox17.Image = Hotspoter.Properties.Resources.Capture;
            btnStartStop.Enabled = !isBusy;
        }

        private void hotspottime_Tick(object sender, EventArgs e)
        {
            OrigTime--;
            label13.Text = OrigTime / 60 + ":" + ((OrigTime % 60) >= 10 ? (OrigTime % 60).ToString() : "0" + OrigTime % 60);

            if (label13.Text == "0:00")
            {
                label13.Text = "Unlimited";
                pictureBox11.Image = Hotspoter.Properties.Resources.oh;
                hostedNetworkManager.StopAsync();
                hotspottime.Enabled = false;

            }
        }

        private void metroButton1_Click(object sender, EventArgs e)
        {
          
            int h = 0;
            int m = 0;
            int s = 0;
            int tt = 0;
            h = (int)numericUpDown1.Value * 3600;
            m = (int)numericUpDown2.Value * 60;
            s = (int)numericUpDown3.Value;
            tt = h + m + s;
            OrigTime = tt;
            bool ttbool = Convert.ToBoolean(tt);
            if (tt == 0)
            {
                MessageBox.Show("Timer Should Be Atleast of 1 sec", "That`s An Error !");
            }
            else if (tt != 0)
            {
                hotspottime.Enabled = true;
            }

        
       

        }

        private void metroButton2_Click(object sender, EventArgs e)
        {
            numericUpDown1.Value = 0;
            numericUpDown2.Value = 0;
            numericUpDown3.Value = 0;

            OrigTime = 0;
            hotspottime.Enabled = false;
            label13.Text = "Unlimited";
        }

        private void txtPass_TextChanged(object sender, EventArgs e)
        {

        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void MenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            
        }

        private void customizeTemplateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("You will need to exit Hotspoter to change the Template. Do you want to close Hotspoter ?", "Confirmation", MessageBoxButtons.YesNo);
            if (result == DialogResult.Yes)
            {
                Process.Start("Template Store.exe");
                Application.Exit();
            }
            else if (result == DialogResult.No)
            {
                //...
            }
        
        }

        private void makersToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            Process.Start("maker.exe");
        }

        private void btnRefresh_Click_3(object sender, EventArgs e)
        {
            hostedNetworkManager.ReadStationUsersAsync();
        }

        
        }
    }


using System;
using System.Collections.Generic;
using System.Timers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KeePass.Plugins;
using System.Windows.Forms;
using KeePass.UI;
using KeePassLib;
using Microsoft.Win32;
using KeePass.Forms;
using System.Security.Cryptography;


namespace Stego
{
    public sealed class StegoExt : Plugin
    {
        private IPluginHost m_host = null;

        private bool checkOut = false;

        private List<string> stegos = new List<string>();

        private System.Timers.Timer beenAWhileTimer;

        private System.Timers.Timer responseTimer;

        private bool screenLocked = false;

        private bool dialogHasResponse = true;

        private enum CheckOutType
        {
            AES,
            DPAPI,
            PlainText
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null) return false;
            m_host = host;
            beenAWhileTimer = new System.Timers.Timer(3600000); //1 hour timer
            //beenAWhileTimer = new System.Timers.Timer(10000); //10 second timer for testing
            beenAWhileTimer.Enabled = false;
            beenAWhileTimer.Elapsed += BeenAWhileTimerIsGoingOff;
            responseTimer = new System.Timers.Timer(300000); //5 minute timer
            //responseTimer = new System.Timers.Timer(5000); //5 second timer for testing
            responseTimer.Enabled = false;
            responseTimer.Elapsed += ResponseTimerIsGoingOff;
            SystemEvents.SessionSwitch += HandleSessionSwitch;
            return true;
        }
        public override void Terminate()
        {
            CheckInStegos();
            beenAWhileTimer.Dispose();
            responseTimer.Dispose();
            beenAWhileTimer.Elapsed -= BeenAWhileTimerIsGoingOff;
            responseTimer.Elapsed -= ResponseTimerIsGoingOff;
            SystemEvents.SessionSwitch -= HandleSessionSwitch;
        }
        public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
        {
            // Provide a menu item for the main location(s)
            if (t != PluginMenuType.Main) return null;

            ToolStripMenuItem tsmi = new ToolStripMenuItem
            {
                Text = "Stego"
            };

            ToolStripMenuItem tsmiAESCheckout = new ToolStripMenuItem
            {
                Text = "AES Check Out"
            };
            tsmiAESCheckout.Click += this.AESCheckOut;
            tsmi.DropDownItems.Add(tsmiAESCheckout);

            ToolStripMenuItem tsmiDPAPICheckOut = new ToolStripMenuItem
            {
                Text = "DPAPI Check Out"
            };
            tsmiDPAPICheckOut.Click += this.DPAPICheckOut;
            tsmi.DropDownItems.Add(tsmiDPAPICheckOut);

            ToolStripMenuItem tsmiPlainTextCheckOut = new ToolStripMenuItem
            {
                Text = "PlainText Check Out"
            };
            tsmiPlainTextCheckOut.Click += this.PlainTextCheckOut;
            tsmi.DropDownItems.Add(tsmiPlainTextCheckOut);

            ToolStripMenuItem tsmiCheckIn = new ToolStripMenuItem
            {
                Text = "Check In"
            };
            tsmiCheckIn.Click += this.CheckIn;
            tsmi.DropDownItems.Add(tsmiCheckIn);


            tsmi.DropDownOpening += delegate (object sender, EventArgs e)
            {

                PwDatabase pd = m_host.Database;
                bool bOpen = ((pd != null) && pd.IsOpen);
                tsmiAESCheckout.Enabled = bOpen && !(checkOut) && dialogHasResponse;
                tsmiDPAPICheckOut.Enabled = bOpen && !(checkOut) && dialogHasResponse;
                tsmiPlainTextCheckOut.Enabled = bOpen && !(checkOut) && dialogHasResponse;
                tsmiCheckIn.Enabled = bOpen && checkOut && dialogHasResponse;               
            };

            return tsmi;

        }   
        
        private void AESCheckOut(object sender, EventArgs e)
        {
            CheckOutStegos(CheckOutType.AES);
            screenLocked = false;
        }

        private void DPAPICheckOut(object sender, EventArgs e)
        {
            CheckOutStegos(CheckOutType.DPAPI);
            screenLocked = false;
        }

        private void PlainTextCheckOut(object sender, EventArgs e)
        {
            CheckOutStegos(CheckOutType.PlainText);
            screenLocked = false;
        }

        private void CheckOutStegos(CheckOutType checkOutType)
        {
            bool atLeastOne = false;
            PwDatabase pd = m_host.Database;            
            
            if ((pd != null) && pd.IsOpen)
            {
                var entries = pd.RootGroup.GetEntries(true); //may want to allow the user to choose subgroup or not
                foreach (var entry in entries)
                {
                    string credu = null;
                    string credp = null;
                    byte[] credub = null;
                    byte[] credpb = null;

                    var credt = entry.Strings.ReadSafe("Title");
                    if (DPAPI)
                    {
                        credub = ProtectedData.Protect(Encoding.UTF8.GetBytes(entry.Strings.ReadSafe("Username")),null, DataProtectionScope.CurrentUser);
                        credpb = ProtectedData.Protect(Encoding.UTF8.GetBytes(entry.Strings.ReadSafe("Password")), null, DataProtectionScope.CurrentUser);
                    }
                    else
                    {
                        credu = entry.Strings.ReadSafe("UserName");
                        credp = entry.Strings.ReadSafe("Password");
                    }
                    
                    try
                    {
                        if (credt != null)
                        {
                            if ((credu != null && !DPAPI) || (credub != null && DPAPI))
                            {
                                if (DPAPI)
                                {
                                    System.Environment.SetEnvironmentVariable($"{credt}_u", Convert.ToBase64String(credub), EnvironmentVariableTarget.User);
                                }
                                else
                                {
                                    System.Environment.SetEnvironmentVariable($"{credt}_u", credu, EnvironmentVariableTarget.User);
                                }
                                
                                stegos.Add($"{credt}_u");
                            }
                            if ((credp != null && !DPAPI) || (credpb != null && DPAPI))
                            {
                                if (DPAPI)
                                {
                                    System.Environment.SetEnvironmentVariable($"{credt}_p", Convert.ToBase64String(credpb), EnvironmentVariableTarget.User);
                                }
                                else
                                {
                                    System.Environment.SetEnvironmentVariable($"{credt}_p", credp, EnvironmentVariableTarget.User);
                                }
                                
                                stegos.Add($"{credt}_p");
                            }
                        }
                        atLeastOne = true;
                    }
                    catch (Exception err)
                    {
                        if (atLeastOne)
                        {
                            MessageBox.Show($"Not all creds were Stego'd\nChecking all back in\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            CheckInStegos();
                        }
                        else
                        {
                            MessageBox.Show($"No creds were Stego'd\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        return;
                    }
                }
                checkOut = true;
                beenAWhileTimer.Enabled = true;
            }
        }

        private void CheckIn(object sender, EventArgs e)
        {
            CheckInStegos();
            beenAWhileTimer.Enabled = false;
        }

        private void CheckInStegos()
        {
            bool failed = false;
            foreach(string stego in stegos)
            {
                try
                {
                    Environment.SetEnvironmentVariable(stego, null, EnvironmentVariableTarget.User);
                }
                catch(Exception err)
                {
                    MessageBox.Show($"Error checking in {stego}\nPlease delete manually\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    failed = true;
                }
            }
            if (failed)
            {
                checkOut = true;
            }
            else
            {
                checkOut= false;
            }
        }

        private void BeenAWhileTimerIsGoingOff(object source, ElapsedEventArgs e)
        {
            responseTimer.Enabled = true;
            beenAWhileTimer.Enabled = false;
            dialogHasResponse = false;
            DialogResult dialogResult = MessageBox.Show("Do you still need your stegos?", "It's been a while", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly);
            if (dialogResult == DialogResult.Yes)
            {         
                if (screenLocked)
                {
                    screenLocked = false;
                    MessageBox.Show("It looks like you locked your screen.\nPlease check out your stegos manually.", "Check Out Manually", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                }
                else if (!(checkOut))
                {
                    beenAWhileTimer.Enabled = true;
                    CheckOutStegos();
                }
                else
                {
                    beenAWhileTimer.Enabled = true;
                }
            }
            else if (dialogResult == DialogResult.No)
            {
                CheckInStegos();
            }
            responseTimer.Enabled = false;
            dialogHasResponse = true;
        }

        private void ResponseTimerIsGoingOff(object source, ElapsedEventArgs e)
        {
            CheckInStegos();
            responseTimer.Enabled = false;
            
        }

        private void HandleSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            CheckInStegos();
            beenAWhileTimer.Enabled = false;
            responseTimer.Enabled = false;
            screenLocked = true;
        }

    }
}

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;

namespace Stego
{
    internal class StegoSession
    {
        private System.Timers.Timer beenAWhileTimer;
        private System.Timers.Timer responseTimer;
        private bool dialogHasResponse = true;
        private bool screenLocked = false;
        private bool checkedOut = false;
        public StegoSession(double beenAWhileTimeInMinutes, double responseTimeInMinutes) 
        {
            beenAWhileTimeInMinutes *= 60000;
            responseTimeInMinutes *= 60000;
            beenAWhileTimer = new System.Timers.Timer(beenAWhileTimeInMinutes); 
            beenAWhileTimer.Elapsed += BeenAWhileTimerIsGoingOff;
            responseTimer = new System.Timers.Timer(responseTimeInMinutes);
            responseTimer.Elapsed += ResponseTimerIsGoingOff;
            SystemEvents.SessionSwitch += HandleSessionSwitch;
        }

        public bool DialogHasResponse { get { return dialogHasResponse; } }

        public bool CheckedOut
        {
            set
            {
                if (value == true)
                {
                    screenLocked = false;
                    beenAWhileTimer.Enabled = true;
                    checkedOut = true;
                }
                else
                {

                }               
            }
            
            get { return checkedOut; }
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
                else if (!(checkedOut))
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

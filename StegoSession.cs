using KeePass.Util.SendInputExt;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using static Stego.StegoExt;

namespace Stego
{
    internal class StegoSession
    {
        private System.Timers.Timer beenAWhileTimer;
        private System.Timers.Timer responseTimer;
        private bool dialogHasResponse = true;
        private bool screenLocked = false;
        private bool checkedOut = false;
        private StegoExt.CheckOutType checkedOutType;
        private List<string> stegos;
        public StegoSession(double beenAWhileTimeInMinutes, double responseTimeInMinutes)
        {
            beenAWhileTimeInMinutes *= 60000;
            responseTimeInMinutes *= 60000;
            beenAWhileTimer = new System.Timers.Timer(beenAWhileTimeInMinutes);
            beenAWhileTimer.Elapsed += BeenAWhileTimerIsGoingOff;
            responseTimer = new System.Timers.Timer(responseTimeInMinutes);
            responseTimer.Elapsed += ResponseTimerIsGoingOff;
            SystemEvents.SessionSwitch += HandleSessionSwitch;
            stegos = new List<string>();
        }

        public event SessionRequiresCheckInHanlder OnSessionRequiresCheckIn;

        public event SessionRequiresCheckOutHanlder OnSessionRequiresCheckOut;

        public bool DialogHasResponse { get { return dialogHasResponse; } }

        public List<string> Stegos { get { return stegos; } }

        public StegoExt.CheckOutType CheckedOutType { get {return checkedOutType; } set {checkedOutType = value; } }

        public bool CheckedOut
        {
            set
            {
                if (value == true)
                {
                    beenAWhileTimer.Enabled = true;
                    checkedOut = true;
                }
                else
                {
                    checkedOut = false;
                }               
            }
            
            get { return checkedOut; }
        }

        public void AddStegos(string stegoName)
        {
            stegos.Add(stegoName);
        }

        public void RemoveStegos(string stegoName)
        {
            stegos.Remove(stegoName);
        }

        public void EndSession()
        {
            beenAWhileTimer.Dispose();
            responseTimer.Dispose();
            beenAWhileTimer.Elapsed -= BeenAWhileTimerIsGoingOff;
            responseTimer.Elapsed -= ResponseTimerIsGoingOff;
            SystemEvents.SessionSwitch -= HandleSessionSwitch;
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
                    if (OnSessionRequiresCheckOut != null)
                    {
                        OnSessionRequiresCheckOut(this, new SessionRequiresCheckOut(checkedOutType));
                    }
                    else
                    {
                        throw new NotImplementedException("No hanlder exists for SessionRequiresCheckOut");
                    }
                }
                else
                {
                    beenAWhileTimer.Enabled = true;
                }
            }
            else if (dialogResult == DialogResult.No)
            {
                if (OnSessionRequiresCheckIn != null)
                {
                    OnSessionRequiresCheckIn(this, new SessionRequiresCheckIn("User no longer needs Stegos"));
                }
                else
                {
                    throw new NotImplementedException("No hanlder exists for SessionRequiresCheckIn");
                }
            }
            responseTimer.Enabled = false;
            dialogHasResponse = true;
        }

        private void ResponseTimerIsGoingOff(object source, ElapsedEventArgs e)
        {
            if (OnSessionRequiresCheckIn != null)
            {
                OnSessionRequiresCheckIn(this, new SessionRequiresCheckIn("User hasn't responded to dialog in time."));
            }
            else
            {
                throw new NotImplementedException("No hanlder exists for SessionRequiresCheckIn");
            }
            responseTimer.Enabled = false;
        }

        private void HandleSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (checkedOut)
            {
                if (OnSessionRequiresCheckIn != null)
                {
                    OnSessionRequiresCheckIn(this, new SessionRequiresCheckIn("User has triggered session switch (locked screen, logged off, etc.)"));
                }
                else
                {
                    throw new NotImplementedException("No hanlder exists for SessionRequiresCheckIn");
                }
                if (!dialogHasResponse)
                {
                    screenLocked = true;
                }               
            }           
        }
    }

    public delegate void SessionRequiresCheckInHanlder(object source, SessionRequiresCheckIn e);
    public class SessionRequiresCheckIn : EventArgs
    {
        private readonly string Reason;
        public SessionRequiresCheckIn(string reason)
        {
            Reason = reason;
        }
        public string GetInfo()
        {
            return Reason;
        }
    }

    public delegate void SessionRequiresCheckOutHanlder(object source, SessionRequiresCheckOut e);
    public class SessionRequiresCheckOut : EventArgs
    {
        private readonly CheckOutType CheckedOutType;
        public SessionRequiresCheckOut(CheckOutType checkedOutType)
        {
            CheckedOutType = checkedOutType;
        }
        public CheckOutType GetInfo()
        {
            return CheckedOutType;
        }

    }
}

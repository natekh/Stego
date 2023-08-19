using System;
using System.IO;
using System.IO.Pipes;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using System.Threading;

namespace Stego
{
    public sealed class StegoExt : Plugin
    {
        private IPluginHost m_host = null;

        private StegoSession session;

        private NamedPipeServerStream pipeServer;

        public enum CheckOutType
        {
            AES,
            DPAPI,
            PlainText
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null) return false;
            m_host = host;
            session = new StegoSession(60, 5);
            session.OnSessionRequiresCheckIn += this.CheckIn;
            session.OnSessionRequiresCheckOut += this.HandlesSessionRequiresCheckOut;
            return true;
        }
        public override void Terminate()
        {
            CheckInStegos();
            session.OnSessionRequiresCheckIn -= this.CheckIn;
            session.OnSessionRequiresCheckOut -= this.HandlesSessionRequiresCheckOut;
            session.EndSession();
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
                tsmiAESCheckout.Enabled = bOpen && !(session.CheckedOut) && session.DialogHasResponse;
                tsmiDPAPICheckOut.Enabled = bOpen && !(session.CheckedOut) && session.DialogHasResponse;
                tsmiPlainTextCheckOut.Enabled = bOpen && !(session.CheckedOut) && session.DialogHasResponse;
                tsmiCheckIn.Enabled = bOpen && session.CheckedOut && session.DialogHasResponse;               
            };

            return tsmi;

        }   
        
        private void AESCheckOut(object sender, EventArgs e)
        {
            CheckOutStegos(CheckOutType.AES);
        }

        private void DPAPICheckOut(object sender, EventArgs e)
        {
            CheckOutStegos(CheckOutType.DPAPI);
        }

        private void PlainTextCheckOut(object sender, EventArgs e)
        {
            CheckOutStegos(CheckOutType.PlainText);
        }

        private void HandlesSessionRequiresCheckOut(object sender, SessionRequiresCheckOut e)
        {
            CheckOutStegos(e.GetInfo());
        }

        private void CheckOutStegos(CheckOutType checkOutType)
        {
            PwDatabase pd = m_host.Database;
            byte[] randomKey = null;

            if ((pd != null) && pd.IsOpen)
            {
                var entries = pd.RootGroup.GetEntries(true); //may want to allow the user to choose subgroup or not
                foreach (var entry in entries)
                {
                    string credu = null;
                    string credp = null;
                    string credt = null;
                    
                    credt = entry.Strings.ReadSafe("Title");
                    switch (checkOutType)
                    {
                        case CheckOutType.AES:
                            randomKey = GenerateKey();
                            using (Aes myAes = Aes.Create())
                            {
                                credu = EncryptString(entry.Strings.ReadSafe("Username"), randomKey, myAes.Key);
                                credp = EncryptString(entry.Strings.ReadSafe("Username"), randomKey, myAes.Key);
                            }
                            break;
                        case CheckOutType.DPAPI:
                            credu = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(entry.Strings.ReadSafe("Username")), null, DataProtectionScope.CurrentUser));
                            credp = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(entry.Strings.ReadSafe("Password")), null, DataProtectionScope.CurrentUser));
                            if (!SetEnvVar(credt, credu, credp))
                            {
                                return;
                            }
                            else
                            {
                                session.CheckedOutType = CheckOutType.DPAPI;
                            }
                            break;
                        case CheckOutType.PlainText:
                            credu = entry.Strings.ReadSafe("UserName");
                            credp = entry.Strings.ReadSafe("Password");
                            if (!SetEnvVar(credt, credu, credp))
                            {
                                return;
                            }
                            else
                            {
                                session.CheckedOutType = CheckOutType.PlainText;
                            }
                            break;
                    }
                }
                session.CheckedOut = true;
                if (checkOutType == CheckOutType.AES)
                {
                    if (randomKey != null)
                    {
                        try
                        {
                            new Thread(delegate () { OpenPipe(randomKey); }).Start();
                        }
                        catch (Exception err)
                        {
                            MessageBox.Show($"Thread for pipe failed. Checking in Stegos.\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            CheckInStegos();
                        }
                    }
                    else
                    {
                        MessageBox.Show($"There was an issue generating a key.\nChecking in Stegos.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        CheckInStegos();
                    }                  
                }               
            }
        }

        private void OpenPipe(byte[] message)
        {
            while(session.CheckedOut && session.CheckedOutType == CheckOutType.AES)
            {
                try
                {
                    pipeServer = new NamedPipeServerStream("Stego", PipeDirection.Out);
                    pipeServer.WaitForConnection();
                    pipeServer.Write(message, 0, message.Length);
                }
                catch (Exception err)
                {
                    MessageBox.Show($"Pipe failed. Checking in Stegos.\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    CheckInStegos();
                }
            }
        }

        private byte[] GenerateKey()
        {
            const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()-+,./?;:[]{}|=";
            Random rnd = new Random();
            int length = rnd.Next(40, 50);
            byte[] bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(valid[rnd.Next(valid.Length)]);
            }
            return bytes;
        }

        private string EncryptString(string plainText, byte[] key, byte[] IV)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (key == null || key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");
            byte[] encrypted;

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.IV = IV;

                // Create an encryptor to perform the stream transform.
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.
            return Convert.ToBase64String(encrypted);
        }

        private bool SetEnvVar(string credt, string credu, string credp)
        {
            try
            {
                if (credt != null)
                {
                    if (credu != null)
                    {
                        string name = $"{credt}_u";
                        if (CheckForExistingVarSuccess(name))
                        {
                            System.Environment.SetEnvironmentVariable(name, credu, EnvironmentVariableTarget.User);
                            session.AddStegos(name);
                        }
                        else
                        {
                            return false;
                        }
                        
                    }
                    if (credp != null)
                    {
                        string name = $"{credt}_p";
                        if (CheckForExistingVarSuccess(name))
                        {
                            System.Environment.SetEnvironmentVariable(name, credp, EnvironmentVariableTarget.User);
                            session.AddStegos(name);
                        }
                        else
                        {
                            return false;
                        }
                        
                    }
                }
                return true;
            }
            catch (Exception err)
            {
                MessageBox.Show($"Creds weren't Stego'd. An error occured.\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CheckInStegos();
                return false;
            }
        }

        private bool CheckForExistingVarSuccess(string name)
        {
            if (System.Environment.GetEnvironmentVariable(name) != null)
            {
                MessageBox.Show($"{name} already exists as an environment variable.\n Stegos won't overwrite existing variables. Ending CheckOut. Please resolve issue and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CheckInStegos();
                return false;
            }
            return true;
        }

        private void CheckIn(object sender, EventArgs e)
        {
            CheckInStegos();
        }

        private void CheckInStegos()
        {
            bool success = true;
            List<string> successfullyRemoved = new List<string>();
            foreach(string stego in session.Stegos)
            {
                try
                {
                    Environment.SetEnvironmentVariable(stego, null, EnvironmentVariableTarget.User);
                    successfullyRemoved.Add(stego);                                     
                }
                catch(Exception err)
                {
                    MessageBox.Show($"Error checking in {stego}\nPlease delete manually or try again.\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    success = false;
                }
            }
            foreach(string stego in successfullyRemoved)
            {
                session.RemoveStegos(stego);
            }
            if (success)
            {
                session.CheckedOut = false;
            }
            else
            {
                session.CheckedOut = true;
            }
        }
    }
}

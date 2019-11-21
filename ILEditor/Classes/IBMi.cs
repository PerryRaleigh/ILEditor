using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using FluentFTP;
using System.Net.Sockets;
using System.Timers;
using IBM.Data.DB2.iSeries;

namespace ILEditor.Classes
{
    class IBMi
    {
        public static Config CurrentSystem;
        private static FtpClient ClientFTP;
        private static iDB2Connection ClientODBC = null;

        public readonly static Dictionary<string, string> FTPCodeMessages = new Dictionary<string, string>()
        {
            { "425", "Not able to open data connection. This might mean that your system is blocking either: FTP, port 20 or port 21. Please allow these through the Windows Firewall. Check the Welcome screen for a 'Getting an FTP error?' and follow the instructions." },
            { "426", "Connection closed; transfer aborted. The file may be locked." },
            { "426T", "Member was saved but characters have been truncated as record length has been reached." },
            { "426L", "Member was not saved due to a possible lock." },
            { "426F", "Member was not found. Perhaps it was deleted." },
            { "530", "Configuration username and password incorrect." }
        };

        public static void HandleError(string Code, string Message)
        {
            string ErrorMessageText = "";
            switch (Code)
            {
                case "200":
                    ErrorMessageText = "425";
                    break;

                case "425":
                case "426":
                case "530":
                case "550":
                    ErrorMessageText = Code;

                    switch (Code)
                    {
                        case "426":
                            if (Message.Contains("truncated"))
                                ErrorMessageText = "426T";

                            else if (Message.Contains("Unable to open or create"))
                                ErrorMessageText = "426L";

                            else if (Message.Contains("not found"))
                                ErrorMessageText = "426F";

                            break;
                        case "550":
                            if (Message.Contains("not created in"))
                                ErrorMessageText = "550NC";
                            break;
                    }

                    break;
            }

            if (FTPCodeMessages.ContainsKey(ErrorMessageText))
                MessageBox.Show(FTPCodeMessages[ErrorMessageText], "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static FtpDataConnectionType GetFtpDataConnectionType(string Type)
        {
            if (Enum.TryParse(Type, out FtpDataConnectionType result))
                return result;
            else
                return FtpDataConnectionType.AutoActive;
        }

        public static bool IsConnected()
        {
            bool result = false;

            if (ClientODBC.State.ToString() == "Open")
                result = true;

            if (ClientFTP != null)
                result = ClientFTP.IsConnected;

            return result;
        }
        public static string FTPFile = "";
        public static bool Connect(bool OfflineMode = false, string promptedPassword = "")
        {
            string[] remoteSysFTP;
            string[] rmtSystemODBC;
            bool result = false;

            // Establish an ODBC connection
            try
            {
                ClientODBC = new iDB2Connection();
                string passWord = "";
                rmtSystemODBC = CurrentSystem.GetValue("system").Split(':');

                if (promptedPassword == "")
                    passWord = Password.Decode(CurrentSystem.GetValue("password"));
                else
                    passWord = promptedPassword;

                iDB2ConnectionStringBuilder csBuilder = new iDB2ConnectionStringBuilder();
                csBuilder.Add("DATASOURCE", rmtSystemODBC[0]);
                csBuilder.Add("DEFAULTCOLLECTION", "qtemp");
                csBuilder.Add("USERID", CurrentSystem.GetValue("username"));
                csBuilder.Add("PASSWORD", passWord);
                csBuilder.Add("CONNECTIONTIMEOUT", "5");

                ClientODBC.ConnectionString = csBuilder.ConnectionString;
                ClientODBC.Open();
                RemoteCommand($"CHGLIBL LIBL({ CurrentSystem.GetValue("datalibl").Replace(',', ' ')}) CURLIB({ CurrentSystem.GetValue("curlib") })");

                // Establish a FTP connection
                FTPFile = IBMiUtils.GetLocalFile("QTEMP", "FTPLOG", DateTime.Now.ToString("MMddTHHmm"), "txt");
                FtpTrace.AddListener(new TextWriterTraceListener(FTPFile));
                FtpTrace.LogUserName = false;   // hide FTP user names
                FtpTrace.LogPassword = false;   // hide FTP passwords
                FtpTrace.LogIP = false;     // hide FTP server IP addresses

                string password = "";

                remoteSysFTP = CurrentSystem.GetValue("system").Split(':');

                if (promptedPassword == "")
                    password = Password.Decode(CurrentSystem.GetValue("password"));
                else
                    password = promptedPassword;

                ClientFTP = new FtpClient(remoteSysFTP[0], CurrentSystem.GetValue("username"), password);

                if (OfflineMode == false)
                {
                    ClientFTP.UploadDataType = FtpDataType.ASCII;
                    ClientFTP.DownloadDataType = FtpDataType.ASCII;

                    //FTPES is configurable
                    if (IBMi.CurrentSystem.GetValue("useFTPES") == "true")
                        ClientFTP.EncryptionMode = FtpEncryptionMode.Explicit;

                    //Client.DataConnectionType = FtpDataConnectionType.AutoPassive; //THIS IS THE DEFAULT VALUE
                    ClientFTP.DataConnectionType = GetFtpDataConnectionType(CurrentSystem.GetValue("transferMode"));
                    ClientFTP.SocketKeepAlive = true;

                    if (remoteSysFTP.Length == 2)
                        ClientFTP.Port = int.Parse(remoteSysFTP[1]);

                    ClientFTP.ConnectTimeout = 5000;
                    ClientFTP.Connect();

                    System.Timers.Timer timer = new System.Timers.Timer();
                    timer.Interval = 60000;
                    timer.Elapsed += new ElapsedEventHandler(KeepAliveFunc);
                    timer.Start();
                }
                RemoteCommand($"CHGLIBL LIBL({ CurrentSystem.GetValue("datalibl").Replace(',', ' ')}) CURLIB({ CurrentSystem.GetValue("curlib") })", true);
                result = true;
            }
            catch (Exception e)
            {
                MessageBox.Show("Unable to connect to " + CurrentSystem.GetValue("system") + " - " + e.Message, "Cannot Connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            //Change the user library list on connection
            return result;
        }

        public static void Disconnect()
        {
            if (IsConnected())
            { 
                ClientODBC.Close();
                ClientFTP.Disconnect();
            }
        }

        private static void KeepAliveFunc(object sender, ElapsedEventArgs e)
        {
            bool showError = !ClientFTP.IsConnected;
            if (ClientFTP.IsConnected)
            {
                try {
                    ClientFTP.Execute("NOOP");
                    showError = false;
                }
                catch
                {
                    showError = true;
                }
            }

            if (showError)
                Editor.TheEditor.SetStatus("Warning! You lost connection " + CurrentSystem.GetValue("system") + "!");
        }

        public static string GetSystem()
        {
            if (ClientFTP != null)
                if (ClientFTP.IsConnected)
                    return ClientFTP.SystemType;
                else
                    return "";
            else
                return "";
        }

        //Returns false if successful
        public static bool DownloadFile(string Local, string Lib, string Obj, string Mbr)
        {
            // List of commands.
            Dictionary<string, string> cmdList = new Dictionary<string, string>();
            string srcData;

            // SQL to select the rows from a source member
            cmdList.Add("SQLSRCMBR", "SELECT * FROM " + Lib + "." + Obj);
            // Command to override the source file to QTEMP
            cmdList.Add("OVRDBF", "OVRDBF FILE(" + Obj + ") TOFILE(" + Lib + "/" + Obj +
                    ") MBR(" + Mbr + ")");

            bool Result = false;
            try
            {
                // Check if the local file already exists. If yes, delete it. 
                if (File.Exists(Local))
                {
                    File.Delete(Local);
                }

                RemoteCommand(cmdList["OVRDBF"]);

                using (iDB2Command cmd = new iDB2Command(cmdList["SQLSRCMBR"], ClientODBC))
                {
                    // Execute the command, and get a DataReader in return.
                    iDB2DataReader dr = cmd.ExecuteReader();

                    if (IsConnected())
                    {
                        using (StreamWriter sw = File.CreateText(Local))
                        {
                            // Read each row from the table and display the information.
                            while (dr.Read())
                            {
                                srcData = dr.GetString(2);
                                sw.WriteLine(srcData);
                            }
                        }
                        Result = false;
                    }
                    else
                        Result = true; //error
                }
            }
            catch (Exception e)
            {
                if (e.Data == null)
                {
                    return true; //error
                }
            }
            return Result;
        }
        
        //Returns false if successful
        public static bool DownloadFile(string Local, string Remote)
        {
            bool Result = false;
            try
            {
                if (ClientFTP.IsConnected)
                    Result = !ClientFTP.DownloadFile(Local, Remote, true);
                else
                    return true; //error
            }
            catch (Exception e)
            {
                if (e.InnerException is FtpCommandException)
                {
                    FtpCommandException err = e.InnerException as FtpCommandException;
                    HandleError(err.CompletionCode, err.Message);
                }
                Result = true;
            }

            return Result;
        }

        //Returns true if successful
        public static bool UploadFile(string Local, string Lib, string Obj, string Mbr)
        {
            if (ClientODBC.State.ToString() == "Open")
            {
                // List of commands.
                Dictionary<string, string> cmdList = new Dictionary<string, string>();

                // SQL to select the rows from a source member
                cmdList.Add("SQLSRCMBR", "SELECT * FROM " + Lib + "." + Obj);
                // Command to override the source file to QTEMP
                cmdList.Add("OVRDBF", "OVRDBF FILE(" + Obj + ") TOFILE(" + Lib + "/" + Obj +
                    ") MBR(" + Mbr + ")");

                return true;
            }
            else
                return false;
        }

        //Returns true if successful
        public static bool UploadFile(string Local, string Remote)
        {
            if (ClientFTP.IsConnected)
                return ClientFTP.UploadFile(Local, Remote, FtpExists.Overwrite);
            else
                return false;
        }

        // ------------------------------------------------------------
        // Run a command on IBMi using QCMDEXC
        // cmdText is the command to be executed
        // Client is an open iDB2Connection the command will be run on
        // Returns true if successful
        // ------------------------------------------------------------
        public static bool RemoteCommand(string cmdText)
        {
            if (ClientODBC.State.ToString() == "Open")
            {

                bool response = true;

                // ------------------------------------------------------------
                // Construct a string containing the call to QCMDEXC
                //
                // We have to delimit single quote characters in the 
                // command text with an extra single quote
                // because QCMDEXC uses single quote characters
                // ------------------------------------------------------------
                String pgmParm = "CALL QSYS.QCMDEXC('"
                + cmdText.Replace("'", "''")
                + "', "
                + cmdText.Length.ToString("0000000000.00000")
                + ")";
                // Create a command obj to execute the program or command.
                iDB2Command cmd = new iDB2Command(pgmParm, ClientODBC);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    response = false;
                }

                // Dispose the command since we're done with it.
                cmd.Dispose();

                // Return the success or failure of the call.
                return response;
            }
            else
            {
                return false;
            }
        }

        //Returns true if successful
        public static bool RemoteCommand(string Command, bool ShowError = true)
        {
            if (ClientFTP.IsConnected)
            {
                string inputCmd = "RCMD " + Command;
                //IF THIS CRASHES CLIENT DISCONNECTS!!!
                FtpReply reply = ClientFTP.Execute(inputCmd);

                if (ShowError)
                    HandleError(reply.Code, reply.ErrorMessage);

                return reply.Success;
            }
            else
            {
                return false;
            }
        }

        public static string RemoteCommandResponse(string Command)
        {
            if (ClientFTP.IsConnected)
            {
                string inputCmd = "RCMD " + Command;
                FtpReply reply = ClientFTP.Execute(inputCmd);

                if (reply.Success)
                    return "";
                else
                    return reply.ErrorMessage;
            }
            else
            {
                return "Not connected.";
            }
        }

        //Returns true if successful
        public static bool RunCommands(string[] Commands)
        {
            bool result = true;
            if (ClientFTP.IsConnected)
            {
                foreach (string Command in Commands)
                {
                    if (RemoteCommand(Command) == false)
                        result = false;
                }
            }
            else
            {
                result = false;
            }

            return result;
        }

        public static bool FileExists(string remoteFile)
        {
            return ClientFTP.FileExists(remoteFile);
        }
        public static bool DirExists(string remoteDir)
        {
            try
            {
                return ClientFTP.DirectoryExists(remoteDir);
            }
            catch (Exception ex)
            {
                Editor.TheEditor.SetStatus(ex.Message + " - please try again.");
                return false;
            }
        }
        public static FtpListItem[] GetListing(string remoteDir)
        {
            return ClientFTP.GetListing(remoteDir);
        }

        public static string RenameDir(string remoteDir, string newName)
        {
            string[] pieces = remoteDir.Split('/');
            pieces[pieces.Length - 1] = newName;
            newName = String.Join("/", pieces);

            if (ClientFTP.MoveDirectory(remoteDir, String.Join("/", pieces)))
                return newName;
            else
                return remoteDir;
        }
        public static string RenameFile(string remoteFile, string newName)
        {
            string[] pieces = remoteFile.Split('/');
            pieces[pieces.Length - 1] = newName;
            newName = String.Join("/", pieces);

            if (ClientFTP.MoveFile(remoteFile, newName))
                return newName;
            else
                return remoteFile;
        }

        public static void DeleteDir(string remoteDir)
        {
            ClientFTP.DeleteDirectory(remoteDir, FtpListOption.AllFiles);
        }

        public static void DeleteFile(string remoteFile)
        {
            ClientFTP.DeleteFile(remoteFile);
        }

        public static void SetWorkingDir(string RemoteDir)
        {
            ClientFTP.SetWorkingDirectory(RemoteDir);
        }
        public static void CreateDirecory(string RemoteDir)
        {
            ClientFTP.CreateDirectory(RemoteDir);
        }
        public static void UploadFiles(string RemoteDir, string[] Files)
        {
            ClientFTP.UploadFiles(Files, RemoteDir, FtpExists.Overwrite, true);
        }
    }
}

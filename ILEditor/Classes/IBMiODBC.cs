using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using System.Timers;
//-------------------------------------------------------------------
// The documentation on how to add the IBM i ODBC packages is here
// https://www-01.ibm.com/support/docview.wss?uid=nas8N1022304
//-------------------------------------------------------------------
using IBM.Data.DB2.iSeries;

namespace ILEditor.Classes
{
    class IBMiODBC
    {
        public static Config CurrentSystem;
        private static iDB2Connection Client = null;

        public static bool Connect(bool OfflineMode = false, string promptedPassword = "")
        {
            string[] remoteSystem;
            bool result = false;
            
            try
            {
                Client = new iDB2Connection();
                string passWord = "";
                remoteSystem = CurrentSystem.GetValue("system").Split(':');

                if (promptedPassword == "")
                    passWord = Password.Decode(CurrentSystem.GetValue("password"));
                else
                    passWord = promptedPassword;

                iDB2ConnectionStringBuilder csBuilder = new iDB2ConnectionStringBuilder();
                csBuilder.Add("DATASOURCE", remoteSystem[0]);
                csBuilder.Add("DEFAULTCOLLECTION", "qtemp");
                csBuilder.Add("USERID", CurrentSystem.GetValue("username"));
                csBuilder.Add("PASSWORD", passWord);
                csBuilder.Add("CONNECTIONTIMEOUT", "5");

                Client.ConnectionString = csBuilder.ConnectionString;
                Client.Open();

                //Change the user library list on connection
                RemoteCommand($"CHGLIBL LIBL({ CurrentSystem.GetValue("datalibl").Replace(',', ' ')}) CURLIB({ CurrentSystem.GetValue("curlib") })");
                result = IsConnected();
            }
            catch (Exception e)
            {
                MessageBox.Show("Unable to connect to " + CurrentSystem.GetValue("system") + " - " + e.Message, "Cannot Connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return result;
        }

        private static bool IsConnected()
        {
            bool result = false;

            if (Client.State.ToString() == "Open")
                result = true;

            return result;
        }

        public static void Disconnect()
        {
            if (IsConnected())
                Client.Close();
        }

        public static string GetSystem()
        {
            if (Client != null)
                if (IsConnected())
                    return Client.ServerVersion;
                else
                    return "";

            else
                return "";

        }

        public static bool DownloadFile(string Local, string Lib, string Obj, string Mbr)
        {
            // List of commands.
            Dictionary<string, string> cmdList = new Dictionary<string, string>();

            // Command to copye the source file and selected member to QTEMP
            cmdList.Add("CPYMBR", "CPYF FROMFILE(" + Lib + "/" + Obj + ") TOFILE(QTEMP/" + Obj + ") " +
                    "FROMMBR(" + Mbr + ") TOMBR(" + Mbr + ") CRTFILE(*YES)");

            // SQL to select the rows from a source member
            cmdList.Add("SQLSRCMBR", "SELECT * FROM " + Obj);

            // Command to override the source file to QTEMP
            cmdList.Add("OVRDBF", "OVRDBF FILE(" + Obj + ") TOFILE(QTEMP/" + Obj + 
                    ") MBR(" + Mbr + ")");

            bool Result = false;
            try
            {
                if (IsConnected())
                    return false;
                else
                    return true; //error

            }
            catch (Exception e)
            {

            }

            return Result;
        }

        // ------------------------------------------------------------
        // Run a command on IBMi using QCMDEXC
        // cmdText is the command to be executed
        // Client is an open iDB2Connection the command will be run on
        // Returns true if successful
        // ------------------------------------------------------------
        public static bool RemoteCommand(string cmdText, bool ShowError = true)
        {

            if (Client.State.ToString() == "Open")
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
                iDB2Command cmd = new iDB2Command(pgmParm, Client);
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


    }
}

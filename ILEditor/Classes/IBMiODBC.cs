using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IBM.Data.DB2.iSeries;

namespace ILEditor.Classes
{
    class IBMiODBC
    {
        public static Config CurrentSystem;
        private static iDB2Connection Client = null;

        public static bool Connect(bool OfflineMode = false, string promptedPassword = "")
        {
            Client = new iDB2Connection();
            string passWord = "";

            if (promptedPassword == "")
                passWord = Password.Decode(CurrentSystem.GetValue("password"));
            else
                passWord = promptedPassword;

            iDB2ConnectionStringBuilder csBuilder = new iDB2ConnectionStringBuilder();
            csBuilder.Add("DATASOURCE", CurrentSystem.GetValue("datasource"));
            csBuilder.Add("DEFAULTCOLLECTION", "qtemp");
            csBuilder.Add("USERID", CurrentSystem.GetValue("username"));
            csBuilder.Add("PASSWORD", passWord);
            csBuilder.Add("CONNECTIONTIMEOUT", "5");

            Client.ConnectionString = csBuilder.ConnectionString;
            Client.Open();

            if (Client.State.ToString() == "Open")
                return true;
            else
                return false;
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

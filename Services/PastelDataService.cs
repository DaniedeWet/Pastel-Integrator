using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.Odbc;

namespace PastelIntegrator.Services
{
    public class PastelDataService
    {
        private readonly string _connectionString;

        public PastelDataService(string dsnName)
        {
            _connectionString = $"DSN={dsnName};";
        }

        private DataTable ExecuteQuery(string sql)
        {
            DataTable dt = new DataTable();

            using (var conn = new OdbcConnection(_connectionString))
            {
                conn.Open();

                using (var cmd = new OdbcCommand(sql, conn))
                using (var da = new OdbcDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            return dt;
        }

        public DataTable GetCustomers()
        {
            string sql = @"
                SELECT *
                FROM CustomerMaster
                ORDER BY CustomerCode";

            return ExecuteQuery(sql);
        }

        public DataTable GetSuppliers()
        {
            string sql = @"
                SELECT *
                FROM SupplierMaster";

            return ExecuteQuery(sql);
        }

        public DataTable GetInventory()
        {
            string sql = @"
                SELECT *
                FROM InventoryMaster";

            return ExecuteQuery(sql);
        }

        public DataTable GetGLAccounts()
        {
            string sql = @"
                SELECT *
                FROM GeneralLedger";

            return ExecuteQuery(sql);
        }

        public bool TestConnection()
        {
            try
            {
                using (var conn =
                    new OdbcConnection(_connectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
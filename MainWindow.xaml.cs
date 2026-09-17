using Microsoft.Win32;
// Adjust if your interop namespace differs (from Object Browser)
using System;
using System.ComponentModel;
using System.Data;
using System.Data.Odbc;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Management;   // add reference to System.Management
using System.Text;
using System.Windows;
using System.Windows.Forms;

namespace PastelIntegrator
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private OdbcConnection _conn;
        private bool _connected;

        public string ProcessBitness => $"Process: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}";
        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();

            LoadSystemDSNs();

            StatusText.Text = "Status: Idle";
        }

        // ---------------------------
        // DSN Loader
        // ---------------------------
        private void LoadSystemDSNs()
        {
            CmbDSN.Items.Clear();

            var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\WOW6432Node\ODBC\ODBC.INI\ODBC Data Sources");

            if (key != null)
            {
                foreach (string name in key.GetValueNames())
                {
                    CmbDSN.Items.Add(name);
                }
            }

            if (CmbDSN.Items.Count > 0)
                CmbDSN.SelectedIndex = 0;
        }

        private void BtnRefreshDSN_Click(object sender, RoutedEventArgs e)
        {
            LoadSystemDSNs();
        }

        // ---------------------------
        // Connect / Disconnect
        // ---------------------------
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbDSN.SelectedItem == null)
                {
                    System.Windows.MessageBox.Show("Select a Data Source.");
                    return;
                }

                string dsn = CmbDSN.SelectedItem.ToString();

                _conn = new OdbcConnection($"DSN={dsn};");
                _conn.Open();

                _connected = true;

                BtnConnect.IsEnabled = false;
                BtnDisconnect.IsEnabled = true;
                BtnFetch.IsEnabled = true;

                StatusText.Text = $"Status: Connected to {dsn}";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message,
                    "ODBC Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_conn != null)
            {
                _conn.Close();
                _conn.Dispose();
                _conn = null;
            }

            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;
            BtnFetch.IsEnabled = false;

            StatusText.Text = "Status: Disconnected";
        }

        // ---------------------------
        // Fetch all customers 
        // ---------------------------
        private void BtnFetch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_conn == null)
                {
                    System.Windows.MessageBox.Show("Please connect first.");
                    return;
                }

                DataTable dt = new DataTable();

                string sql =
                @"SELECT
             CustomerCode,
             CustomerDesc,
             BalanceThis13
          FROM CustomerMaster
          ORDER BY CustomerCode";

                using (var cmd = new OdbcCommand(sql, _conn))
                using (var da = new OdbcDataAdapter(cmd))
                {
                    da.Fill(dt);
                }

                GridCustomers.ItemsSource = dt.DefaultView;

                StatusText.Text =
                    $"Status: Loaded {dt.Rows.Count:N0} customers";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message,
                    "Customer Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ---------------------------
        // Export to pipe-delimited text
        // ---------------------------
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Export CSV will be implemented later.",
                "PastelIntegrator",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        // ---------------------------
        // Utilities
        // ---------------------------

        private void SafeCleanup()
        {
            try { /* no session files to close for Get*/ } catch { }
            _connected = false;
            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;
            BtnFetch.IsEnabled = false;
            BtnExport.IsEnabled = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SafeCleanup();
            base.OnClosing(e);
        }
       

        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
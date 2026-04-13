using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

// Adjust if your interop namespace differs (from Object Browser)
using PasSDK;

namespace PastelSdkClient32
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // COM coclass (early-bound)
        private PastelPartnerSDK _sdk;

        private bool _connected;
        private readonly ObservableCollection<CustomerRow> _rows = new ObservableCollection<CustomerRow>();

        // Constants from the SDK file layouts (ACCMASD / ACCDELIV)
        // accmasd = customer master (Key 0 = Number, len 6)
        // accdeliv = delivery (Key 0 = Number + Code, Code len 3), Code "   " (3 spaces) = default
        private const string FileCustomer = "accmasd";
        private const int KeyCustomerByNumber = 0;
        private const int CustomerNumberLen = 6;

        private const string FileDelivery = "accdeliv";
        private const int KeyDeliveryByNumberCode = 0;
        private const int DeliveryCodeLen = 3;

        public string ProcessBitness => $"Process: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}";
        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            GridCustomers.ItemsSource = _rows;
            DataContext = this;
            GridCustomers.MouseDoubleClick += (_, __) => ShowPickedCustomer();

            StatusText.Text = "Status: Idle";
        }

        // ---------------------------
        // Connect / Disconnect
        // ---------------------------
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureX86(); // The SDK is 32-bit only

                _sdk = new PastelPartnerSDK();

                // 1) SetDataPath is the required first call for the SDK
                //    It points the SDK at the company data folder.  (SDK docs)

                string dataPath = System.IO.Path.Combine(TxtCompany.Text.Trim(), "2026");
                string rc = _sdk.SetDataPath(dataPath);

                if (rc != "0")
                {
                    throw new ApplicationException(
                        $"SetDataPath failed.\nPath: {dataPath}\nSDK returned: {rc}"
                    );
                }

                //string rc = _sdk.SetDataPath(TxtCompany.Text.Trim());
                
                //ExpectOk(rc, "SetDataPath"); // many SDK functions return "0" on success

                // 2) Optional: SetLicense if you have Serial/Auth (unlocks full SDK)
                //    If blanks, we skip; SDK will run in demo mode if unlicensed.
                //    Format: SetLicense(serial, pAuthcode) per docs.
                if (!string.IsNullOrWhiteSpace(TxtUser.Text) && !string.IsNullOrWhiteSpace(TxtPass.Password))
                {

                    string licensee = TxtUser.Text.Trim();
                    string authCode = TxtPass.Password;

                    // SetLicense returns VOID, uses ref params
                    _sdk.SetLicense(ref licensee, ref authCode);


                    // rc = _sdk.SetLicense(TxtUser.Text.Trim(), TxtPass.Password);
                    //ExpectOk(rc, "SetLicense");
                }

                _connected = true;
                BtnConnect.IsEnabled = false;
                BtnDisconnect.IsEnabled = true;
                BtnFetch.IsEnabled = true;
                BtnExport.IsEnabled = false;

                StatusText.Text = "Status: Connected (data path set)";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Status: Connect failed";
                MessageBox.Show(ex.Message, "Pastel SDK", MessageBoxButton.OK, MessageBoxImage.Error);
                SafeCleanup();
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            SafeCleanup();
            StatusText.Text = "Status: Disconnected";
        }

        // ---------------------------
        // Fetch all customers (pipe rows via GetNearest/GetNext)
        // ---------------------------
        private void BtnFetch_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected)
            {
                MessageBox.Show("Connect first.", "Pastel SDK", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _rows.Clear();

                // Tip from SDK docs: to read the whole file, use GetNearest with an ASCII-zero key;
                // then call GetNext with the same key number to iterate.  (GetNearest/GetNext)
                // We'll build a starting key for Key 0 (Number, length 6) filled with ASCII zeros.
                string startKey = AsciiZeros(CustomerNumberLen);

                string row = _sdk.GetNearest(FileCustomer, KeyCustomerByNumber, startKey);

                // Iterate until EOF (error "9") or no more pipe rows
                int safety = 0;
                while (IsRecordRow(row))
                {
                    var cust = ParseCustomerMaster(row);

                    // Try to fetch default delivery/contact for this account from ACCDELIV
                    string delivKey = RightPad(cust.Account, CustomerNumberLen) + new string(' ', DeliveryCodeLen); // Number + "   "
                    string deliv = _sdk.GetRecord(FileDelivery, KeyDeliveryByNumberCode, delivKey);
                    if (IsRecordRow(deliv))
                    {
                        var (contact, phone) = ParseDeliveryContact(deliv);
                        cust.Contact = contact;
                        cust.Telephone = phone;
                        cust.Pipe = BuildPipeDisplay(cust); // include phone/contact in the display pipe
                    }

                    _rows.Add(cust);

                    // next record
                    row = _sdk.GetNext(FileCustomer, KeyCustomerByNumber);

                    if (++safety > 1_000_000) break; // absolute safety
                }

                BtnExport.IsEnabled = _rows.Count > 0;
                StatusText.Text = $"Status: Retrieved {_rows.Count} customers";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fetch error: {ex.Message}", "Pastel SDK", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------------------
        // Export to pipe-delimited text
        // ---------------------------
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0) return;

            var sfd = new SaveFileDialog
            {
                Title = "Export customers (pipe-delimited)",
                Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = "Pastel_Customers.txt",
                OverwritePrompt = true
            };

            if (sfd.ShowDialog(this) == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Account|Description|Telephone|Contact|PipeDelimited");
                foreach (var r in _rows)
                    sb.AppendLine($"{r.Account}|{r.Description}|{r.Telephone}|{r.Contact}|{r.Pipe}");
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                StatusText.Text = $"Status: Exported {_rows.Count} rows → {sfd.FileName}";
            }
        }

        // ---------------------------
        // Pick behaviour (double-click)
        // ---------------------------
        private void ShowPickedCustomer()
        {
            if (GridCustomers.SelectedItem is CustomerRow row && !string.IsNullOrWhiteSpace(row.Account))
            {
                try
                {
                    // Exact read by account code using Key 0 on ACCMASD
                    string key = RightPad(row.Account, CustomerNumberLen);
                    string rec = _sdk.GetRecord(FileCustomer, KeyCustomerByNumber, key);
                    if (!IsRecordRow(rec))
                    {
                        MessageBox.Show("Customer not found (record read failed).", "Pastel SDK",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var cust = ParseCustomerMaster(rec);

                    // Re-read delivery (Number + "   ")
                    string delivKey = RightPad(cust.Account, CustomerNumberLen) + new string(' ', DeliveryCodeLen);
                    string deliv = _sdk.GetRecord(FileDelivery, KeyDeliveryByNumberCode, delivKey);
                    if (IsRecordRow(deliv))
                    {
                        var (contact, phone) = ParseDeliveryContact(deliv);
                        cust.Contact = contact;
                        cust.Telephone = phone;
                        cust.Pipe = BuildPipeDisplay(cust);
                    }

                    MessageBox.Show(
                        $"Account : {cust.Account}\n" +
                        $"Name    : {cust.Description}\n" +
                        $"Phone   : {cust.Telephone}\n" +
                        $"Contact : {cust.Contact}\n\n" +
                        $"RAW: {cust.Pipe}",
                        "Picked Customer",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lookup error for [{row.Account}]: {ex.Message}", "Pastel SDK",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ---------------------------
        // Parsing helpers (pipe rows)
        // ---------------------------

        // ACCMASD: Category(1), Number(2), Description(3), ...
        // We'll take Number & Description for display.
        private static CustomerRow ParseCustomerMaster(string pipeRow)
        {
            var t = (pipeRow ?? "").Split('|');
            var account = t.ElementAtOrDefault(1) ?? "";     // Number (6 chars)
            var desc = t.ElementAtOrDefault(2) ?? "";     // Description

            // We’ll fill phone/contact later from ACCDELIV
            return new CustomerRow
            {
                Account = account.Trim(),
                Description = desc.Trim(),
                Telephone = "",
                Contact = "",
                Pipe = pipeRow
            };
        }

        // ACCDELIV columns include: Number(1), Code(2), Salesman(3), Contact(4), Telephone(5), ...
        private static (string contact, string phone) ParseDeliveryContact(string pipeRow)
        {
            var t = (pipeRow ?? "").Split('|');
            string contact = (t.ElementAtOrDefault(3) ?? "").Trim();
            string phone = (t.ElementAtOrDefault(4) ?? "").Trim();
            return (contact, phone);
        }

        private static string BuildPipeDisplay(CustomerRow r)
            => $"{r.Account}|{r.Description}|{r.Telephone}|{r.Contact}";

        // ---------------------------
        // SDK return handling
        // ---------------------------
        private static void ExpectOk(string rc, string method)
        {
            // Many non-read calls return "0" on success; else "code|detail" (per docs).
            // We'll treat a bare "0" as success and anything else as error.

            if (!string.Equals(rc, "", StringComparison.Ordinal))
                throw new ApplicationException($"{method} failed: {rc}");

           
        }

        private static bool IsRecordRow(string s)
        {
            // For GetRecord/GetNearest/GetNext, data rows are pipe-delimited.
            // Bare numeric values (e.g. "9") indicate errors like EOF.
            return !string.IsNullOrWhiteSpace(s) && s.Contains("|");
        }

        // ---------------------------
        // Utilities
        // ---------------------------
        private static string RightPad(string value, int len)
        {
            value = value ?? "";
            return (value.Length >= len) ? value.Substring(0, len) : value + new string(' ', len - value.Length);
        }

        private static string AsciiZeros(int len) => new string('\0', len);

        private void EnsureX86()
        {
            if (Environment.Is64BitProcess)
                throw new InvalidOperationException("The app must run as 32-bit (x86) to load the Pastel Partner SDK.");
        }

        private void SafeCleanup()
        {
            try { /* no session files to close for Get*/ } catch { }
            _connected = false;
            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;
            BtnFetch.IsEnabled = false;
            BtnExport.IsEnabled = false;
            _sdk = null;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SafeCleanup();
            base.OnClosing(e);
        }

        // ---------------------------
        // Model for the grid
        // ---------------------------
        public sealed class CustomerRow
        {
            public string Account { get; set; }
            public string Description { get; set; }
            public string Telephone { get; set; }
            public string Contact { get; set; }
            public string Pipe { get; set; }
        }

        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
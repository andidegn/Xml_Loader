using Config_Downloader.View;
using ECTunes.Database.ConfigLibrary;
using ECTunes.Database.Util;
using ECTunes.Util;
using ECTunesDB.model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Security.Principal;
using System.Windows.Threading;

namespace Config_Downloader {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        private CarDC car;
        private CustomerDC customer;
        private List<ParamDC> param;
        private List<SignalDC> signal;
        private bool databaseCarSelected;
        private bool xmlCarSelected;
        private String xmlCarSelectedCustomer;
        private String xmlCarSelectedCar;
        private DateTime xmlCarSelectedVersion;

        private String filePath;

        private const String ERROR_NO_CAR_SELECTED = "A car has to be selected in order to save it to an xml file.\nTo Select a car, first expand a customer, then double-click on the name of the car in the treeview to the left.";
        private const int RETRY_ATTEMPTS = 3;
        private const String CAR_VERSION_SEPERATOR = " - ";

        /// <summary>
        /// 0: '127.0.0.1'<br />
        /// 1: '192.168.0.101'<br />
        /// 2: '188.180.104.131'<br />
        /// 3: 'andidegn.dk'
        /// </summary>
        private const int IP_INDEX = 2;

        String connectionString = "tcp://" + DbConnector.IP[IP_INDEX] + ":" + DbConnector.PORT + "/" + DbConnector.SERVER_PATH;
        private const bool REQUIRE_LOGIN = IP_INDEX == 1 || IP_INDEX == 2 ? true : false;

        private Thread connectThread;

        public static bool DEBUG = true;

        private bool connected;
        private bool loggedIn = false;
        private String channelName = "tcp";

        private IDbConnectorRemote db;

        public enum Source {
            DB,
            XML
        };

        public MainWindow() {
            InitializeComponent();
            InitRest();
            if (DEBUG) lblConnectionString.Content = connectionString;
        }

        private void InitRest() {
            filePath = XmlParser.DEFAULT_FILE_PATH;
            String password = ShowPasswordDialog(null);
            if (password == null)
                Exit();
            SetupChannel(password);
            databaseCarSelected = false;
            UpdateXmlTreeview();
            connectThread = new Thread(new ThreadStart(() => TryConnect()));
            connectThread.SetApartmentState(ApartmentState.STA);
            connectThread.Start();
        }

        #region Dialogs
        /// <summary>
        /// Opens a file dialog box to select the desired xml file
        /// </summary>
        /// <returns></returns>
        private String ShowOpenFileDialog() {
            String path = "";

            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Filter = "XML Files (.xml;.txt)|*.xml;*.txt|All Files (*.*)|*.*";
            ofd.FilterIndex = 1;

            Nullable<bool> result = ofd.ShowDialog();
            if (result == true) {
                path = ofd.InitialDirectory + ofd.FileName;
            }
            return path;
        }

        private String ShowPasswordDialog(String title) {
            LoginScreen ls = new LoginScreen(title);
            ls.ShowDialog();
            if (ls.DialogResult.HasValue && ls.DialogResult.Value)
                return ls.GetPassword();
            else
                return null;
        }
        #endregion

        #region Connection
        private void SetupChannel(String password) {
            IDictionary props = new Hashtable();
            props["port"] = 0;
            props["name"] = channelName;
            props["timeout"] = 2000;
            props["retryCount"] = 15;
            if (REQUIRE_LOGIN) {
                props["username"] = DbConnector.USERNAME;
                props["password"] = password;
            }

            BinaryClientFormatterSinkProvider provider = new BinaryClientFormatterSinkProvider();
            TcpClientChannel channel = new TcpClientChannel(props, provider);
            if (ChannelServices.GetChannel(channelName) != null)
                ChannelServices.UnregisterChannel(ChannelServices.GetChannel(channelName));
            ChannelServices.RegisterChannel(channel, true);
            loggedIn = true;
        }

        private delegate void udbvDelegate();

        private void TryConnect() {
            while (true) {
                while (loggedIn && !connected) {
                    try {
                        db = (IDbConnectorRemote)Activator.GetObject(typeof(IDbConnectorRemote), connectionString);
                        connected = db.IsConnected();
                        Dispatcher.BeginInvoke(new udbvDelegate(UpdateDatabaseTreeView));
                    }
                    catch (InvalidCredentialException ex1) {
                        Exit();
                        loggedIn = false;
                    }
                    catch (Exception ex2) {
                        Thread.Sleep(2500);
                    }
                }
                Thread.Sleep(500);
            }
        }

        private void NoConnection(String function, Exception msg) {
            connected = false;
            gridConnected.Background = Brushes.Red;
            StringBuilder sb = new StringBuilder();
            sb.Append("No connection to the server!");
            if (DEBUG) {
                sb.Append("\nError in: '");
                sb.Append(function);
                sb.Append("'\nException:\n");
                sb.Append(msg.Data);
                sb.Append("\nMsg:\n");
                sb.Append(msg.Message);
                sb.Append("\nStackTrace:\n");
                sb.Append(msg.StackTrace);
                sb.Append("\nInnerException:\n");
                sb.Append(msg.InnerException);
            }
            MessageBox.Show(sb.ToString());
        }

        private void Exit() {
            if (connectThread != null)
                connectThread.Abort();
            if (Dispatcher.CheckAccess())
                Application.Current.Shutdown();
            else
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new ThreadStart(() => Application.Current.Shutdown()));
        }
        #endregion

        #region Upload / Download / Delete
        /// <summary>
        /// Saves a customer, a car, a parameter list and a signal list to the the xml file
        /// </summary>
        private void SaveToXml() {
            XmlParser.SaveToXml(customer, car, param, signal);
            UpdateXmlTreeview();
        }

        /// <summary>
        /// Saves a customer, a car, a parameter list and a signal list to the database
        /// </summary>
        /// <param name="carName"></param>
        private void SaveToDatabase() {
            if (connected)
                try {
                    if (XmlParser.SaveToDatabase(db, xmlCarSelectedCustomer, xmlCarSelectedCar, xmlCarSelectedVersion)) {
                        ResetSelectedCar();
                        UpdateXmlTreeview();
                        UpdateDatabaseTreeView();
                    }
                }
                catch (Exception ex) {
                    NoConnection("SaveToDatabase()", ex);
                }
        }

        private void Delete(Source source) {
            TreeViewItem selectedNode = null;
            if (source == Source.DB)
                selectedNode = tvDatabaseView.SelectedItem as TreeViewItem;
            else
                selectedNode = tvXmlView.SelectedItem as TreeViewItem;
            if (selectedNode == null)
                return;
            if (ShowPasswordDialog("Please type password to Delete!") == "yt")
                if (selectedNode.Parent is TreeViewItem) {
                    String customerName = (selectedNode.Parent as TreeViewItem).Header.ToString();
                    String carName;
                    DateTime version;
                    SplitNameVersion(selectedNode, out carName, out version);
                    if (ShowConfirmDelete("car", selectedNode.Header.ToString())) {
                        if (source == Source.DB ? db.DeleteCar(customerName, carName, version) : XmlParser.DeleteCar(customerName, carName, version)) {
                            databaseCarSelected = false;
                            UpdateXmlTreeview();
                            UpdateDatabaseTreeView();
                        }
                        else
                            MessageBox.Show("An error has occurred!");
                    }
                }
                else {
                    String customerName = selectedNode.Header.ToString();
                    if (ShowConfirmDelete("customer", customerName) && MessageBox.Show("Are you absolutely sure you want to delete this customer?\n All this customers cars will also be deleted!", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) {
                        if (source == Source.DB ? db.DeleteCustomer(customerName) : XmlParser.DeleteCustomer(customerName)) {
                            databaseCarSelected = false;
                            UpdateXmlTreeview();
                            UpdateDatabaseTreeView();
                        }
                        else
                            MessageBox.Show("An error has occurred!");
                    }
                }
        }

        private void ResetSelectedCar() {
            tbxXmlCarSelectedCar.Clear();
            tbxXmlCarSelectedCustomer.Clear();
            tbxXmlCarSelectedVersion.Clear();
            xmlCarSelectedCustomer = null;
            xmlCarSelectedCar = null;
            xmlCarSelectedVersion = DateTime.Now;
        }
        #endregion

        #region GUI Update
        /// <summary>
        /// Updates the treeview of the database
        /// </summary>
        private void UpdateDatabaseTreeView() {
            tvDatabaseView.Items.Clear();
            if (connected) {
                gridConnected.Background = Brushes.Green;
                try {
                    List<CustomerDC> customerQuery = db.GetCustomers();
                    foreach (var customer in customerQuery) {
                        TreeViewItem cus = new TreeViewItem();
                        cus.Header = customer.name;
                        List<CarDC> carQuery = db.GetCars(customer.customerId);
                        foreach (var car in carQuery) {
                            cus.Items.Add(new TreeViewItem() { Header = car.name + CAR_VERSION_SEPERATOR + car.version.ToString(XmlParser.VERSION_FORMAT) });
                        }
                        tvDatabaseView.Items.Add(cus);
                    }
                }
                catch (Exception ex) {
                    NoConnection("UpdateDatabaseTreeView()", ex);
                }
            }
        }

        /// <summary>
        /// Updates the treeview of the XML file
        /// </summary>
        private void UpdateXmlTreeview() {
            tvXmlView.Items.Clear();
            try {
                foreach (XmlNode customerNode in XmlParser.GetRootNode(filePath)) {
                    TreeViewItem cus = new TreeViewItem();
                    cus.Header = customerNode.Name;
                    foreach (XmlNode carNode in customerNode) {
                        if (carNode.Attributes.Count > 0)
                            cus.Items.Add(new TreeViewItem() { Header = carNode.Name + CAR_VERSION_SEPERATOR + carNode.Attributes[0].Value });
                        else
                            cus.Items.Add(new TreeViewItem() { Header = carNode.Name });
                    }
                    tvXmlView.Items.Add(cus);
                }
            }
            catch (Exception ex) {
                ShowError("An error occurred while trying to read 'myXmlFile.xml'.\n\rPlease make sure the file is accessible in: ..\\files\\\n");
            }
        }
        #endregion

        #region Misc
        private void SplitNameVersion(TreeViewItem selectedNode, out String name, out DateTime version) {
            String[] words = Regex.Split(selectedNode.Header.ToString(), CAR_VERSION_SEPERATOR);
            name = words[0];
            for (int i = 1; i < words.Length - 1; i++)
                name += CAR_VERSION_SEPERATOR + words[i];
            if (words.Length > 1)
                version = XmlParser.ToDateTime(words[words.Length - 1]);
            else
                version = DateTime.MinValue;
        }
        #endregion

        #region MessageBoxes
        private bool ShowConfirmDelete(String type, String objectToDelete) {
            return MessageBox.Show(String.Format("Are you sure you want to delete {0}: '{1}'?\nThis operation cannot be undone!", type, objectToDelete), "Delete", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes ? true : false;
        }

        /// <summary>
        /// Spawns a message box with an error text
        /// </summary>
        /// <param name="text"></param>
        private void ShowError(String text) {
            MessageBox.Show(this, text, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        #endregion

        #region Event Handlers

        #region MouseDoubleClick
        private void tvDatabaseView_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            TreeViewItem selectedNode = tvDatabaseView.SelectedItem as TreeViewItem;
            if (connected && selectedNode != null && selectedNode.Parent as TreeViewItem != null) {
                try {
                    String _customerName = (selectedNode.Parent as TreeViewItem).Header.ToString();
                    String _carName;
                    DateTime _version;
                    SplitNameVersion(selectedNode, out _carName, out _version);
                    customer = db.GetCustomer(_customerName);
                    car = db.GetCar(customer.customerId, _carName, _version);
                    var p = db.GetParamList(car.carId);
                    param = p;
                    dgvDatabaseParam.ItemsSource = param;
                    signal = db.GetSignalList(car.carId);
                    dgcDatabaseSignal.ItemsSource = signal;
                    databaseCarSelected = true;
                }
                catch (Exception ex) {
                    NoConnection("tvDatabaseView_MouseDoubleClick(object sender, MouseEventArgs e)", ex);
                }
            }
        }

        private void tvXmlView_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            TreeViewItem selectedNode = tvXmlView.SelectedItem as TreeViewItem;
            if (selectedNode != null && selectedNode.Parent as TreeViewItem != null) {
                xmlCarSelectedCustomer = (selectedNode.Parent as TreeViewItem).Header.ToString();
                SplitNameVersion(selectedNode, out xmlCarSelectedCar, out xmlCarSelectedVersion);
                tbxXmlCarSelectedCustomer.Text = xmlCarSelectedCustomer;
                tbxXmlCarSelectedCar.Text = xmlCarSelectedCar;
                tbxXmlCarSelectedVersion.Text = xmlCarSelectedVersion.ToString(XmlParser.VERSION_FORMAT);
                xmlCarSelected = true;
            }
        }
        #endregion

        #region Click
        private void btnToXml_Click(object sender, RoutedEventArgs e) {
            if (databaseCarSelected) {
                SaveToXml();
            }
            else
                ShowError(ERROR_NO_CAR_SELECTED);
        }

        private void btnSaveToDatabase_Click(object sender, RoutedEventArgs e) {
            if (xmlCarSelected) {
                SaveToDatabase();
            }
            else
                ShowError(ERROR_NO_CAR_SELECTED);
        }

        private void btnDeleteFromDatabase_Click(object sender, RoutedEventArgs e) {
            Delete(Source.DB);
        }

        private void btnDeleteFromXml_Click(object sender, RoutedEventArgs e) {
            Delete(Source.XML);
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e) {
            databaseCarSelected = false;
            UpdateDatabaseTreeView();
            UpdateXmlTreeview();
        }

        private void btnExit_Click(object sender, RoutedEventArgs e) {
            Application.Current.Shutdown(0);
        }

        private void btnLoadFile_Click(object sender, RoutedEventArgs e) {
            String path = ShowOpenFileDialog();
            if (path.Length > 0) {
                filePath = path;
                UpdateXmlTreeview();
            }
        }
        #endregion

        #region Key-press
        private void tvDatabaseView_KeyUp(object sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Delete:
                    Delete(Source.DB);
                    break;
                default:
                    break;
            }
        }

        private void tvXmlView_KeyUp(object sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Delete:
                    Delete(Source.XML);
                    break;
                default:
                    break;
            }
        }
        #endregion

        #region Other
        private void mainWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            DragMove();
        }

        private void Window_Closing_1(object sender, System.ComponentModel.CancelEventArgs e) {
            Exit();
        }
        #endregion
        #endregion
    }
}

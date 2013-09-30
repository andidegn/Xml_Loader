using ECTunes.Database.ConfigLibrary;
using ECTunes.Database.Util;
using ECTunes.Util;
using ECTunesDB.model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using Config_Downloader.View;
using System.Security.Authentication;

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

        bool connected;
        bool loggedIn;

        IDbConnectorRemote db;

        public enum Source {
            DB,
            XML
        };

        public MainWindow() {





            // Creating the IDictionary to set the port on the channel instance.
            IDictionary props = new Hashtable();
            if (REQUIRE_LOGIN) {
                props["username"] = DbConnector.USERNAME;
                props["password"] = ShowLoginDialog();
            }
            else
                loggedIn = true;


            InitializeComponent();
            InitRest();
            connectThread = new Thread(new ThreadStart(() => TryConnect(props)));
            connectThread.Start();
            if (DEBUG) lblConnectionString.Content = connectionString;
        }

        private String ShowLoginDialog() {
            LoginScreen ls = new LoginScreen();
            ls.ShowDialog();
            if (ls.DialogResult.HasValue && ls.DialogResult.Value) {
                loggedIn = true;
                return ls.GetPassword();
            }
            else {
                loggedIn = false;
                Application.Current.Shutdown();
            }
            return null;
        }

        private void InitRest() {
            databaseCarSelected = false;
            UpdateXmlTreeview();
        }

        private delegate void udbvDelegate();

        private void TryConnect(IDictionary props) {
            // Creating a custom formatter for a TcpChannel sink chain.
            BinaryClientFormatterSinkProvider provider = new BinaryClientFormatterSinkProvider();
            // Pass the properties for the port setting and the server provider in the server chain argument. (Client remains null here.)
            TcpClientChannel channel = new TcpClientChannel(props, provider);

            ChannelServices.RegisterChannel(channel, true);
            //int counter = 0;
            if (loggedIn)
            while (true) {
                if (!connected) {
                    do {
                        try {
                            db = (DbConnector)Activator.GetObject(typeof(DbConnector), connectionString);
                            connected = db.IsConnected();
                        }
                        catch (InvalidCredentialException) {

                        }
                        catch (Exception ex) {
                            Thread.Sleep(2500);
                            //if (counter++ >= RETRY_ATTEMPTS) {
                            //    NoConnection("InitRest()", ex);
                            //    break;
                            //}
                            throw;
                        }
                    } while (!connected);
                    //if (Dispatcher.invokere)
                        Dispatcher.BeginInvoke(new udbvDelegate(UpdateDatabaseTreeView));
                    //else
                    //    UpdateDatabaseTreeView();
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
            //MessageBox.Show("No connection to the server!" + (DEBUG ? "\nException:\n" + msg.Data + "\nMsg:\n" + msg.Message + "\nStackTrace:\n" + msg.StackTrace + "\nInnerException:\n" + msg.InnerException: ""));
        }

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
                } catch (Exception ex) {
                    NoConnection("SaveToDatabase()", ex);
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
                            cus.Items.Add(new TreeViewItem() { Header = car.name + CAR_VERSION_SEPERATOR + car.version.ToString(XmlParser.VERSION_FORMAT) } );
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
                foreach (XmlNode customerNode in XmlParser.GetRootNode()) {
                    TreeViewItem cus = new TreeViewItem();
                    cus.Header = customerNode.Name;
                    foreach (XmlNode carNode in customerNode) {
                        if (carNode.Attributes.Count > 0)
                            cus.Items.Add(new TreeViewItem() { Header = carNode.Name + CAR_VERSION_SEPERATOR + carNode.Attributes[0].Value } );
                        else
                            cus.Items.Add(new TreeViewItem() { Header = carNode.Name } );
                    }
                    tvXmlView.Items.Add(cus);
                }
            } catch (Exception ex) {
                ShowError("An error occurred while trying to read 'myXmlFile.xml'.\n\rPlease make sure the file is accessible in: ..\\files\\\n");
            }
        }

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

        private void Delete(TreeViewItem selectedNode, Source source) {
            if (selectedNode == null)
                return;
            if (selectedNode.Parent is TreeViewItem) {
                String customerName = (selectedNode.Parent as TreeViewItem).Header.ToString();
                String carName;
                DateTime version;
                SplitNameVersion(selectedNode, out carName, out version);
                if (ShowConfirmDelete("car", selectedNode.Header.ToString())) {
                    if ( source == Source.DB ? db.DeleteCar(customerName, carName, version) : XmlParser.DeleteCar(customerName, carName, version)) {
                        InitRest();
                    } else
                        MessageBox.Show("An error has occurred!");
                }
            }
            else {
                String customerName = selectedNode.Header.ToString();
                if (ShowConfirmDelete("customer", customerName) && MessageBox.Show("Are you absolutely sure you want to delete this customer?\n All this customers cars will also be deleted!", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) {
                    if ( source == Source.DB ? db.DeleteCustomer(customerName) : XmlParser.DeleteCustomer(customerName)) {
                        InitRest();
                    } else
                        MessageBox.Show("An error has occurred!");
                }
            }
        }

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

        #region Event Handlers
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
                } catch (Exception ex) {
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
            Delete(tvDatabaseView.SelectedItem as TreeViewItem, Source.DB);
        }

        private void btnDeleteFromXml_Click(object sender, RoutedEventArgs e) {
            Delete(tvXmlView.SelectedItem as TreeViewItem, Source.XML);
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e) {
            databaseCarSelected = false;
            UpdateDatabaseTreeView();
            UpdateXmlTreeview();
        }

        private void btnExit_Click(object sender, RoutedEventArgs e) {
            Application.Current.Shutdown(0);
        }

        private void Window_Closing_1(object sender, System.ComponentModel.CancelEventArgs e) {
            connectThread.Abort();
        }
        #endregion
    }
}

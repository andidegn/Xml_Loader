using ECTunesDB.model;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Xml;

namespace ECTunes.Util {
    public static class XmlParser {

        private static String FILE_PATH;
        private static XmlDocument XD; 
        private static XmlNode ROOT_NODE;
        public static readonly String VERSION_FORMAT = "yyyy-MM-dd HH:mm:ss:fff";

        /// <summary>
        /// Gets the appropriate XmlNode from an xml file
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static XmlNode GetRootNode(String filePath) {
            if (XD == null || !FILE_PATH.Equals(filePath)) {
                FILE_PATH = filePath;
                LoadFile(filePath);
            }
            ROOT_NODE = XD.SelectSingleNode("/Root/ConfigsFinal");
            return ROOT_NODE;
        }

        public static XmlNode GetRootNode() {
            return GetRootNode(@"files\myXmlFile.xml");
        }

        private static void LoadFile(String filePath) {
            XD = new XmlDocument();
            XD.Load(filePath);
        }

        #region Populate Xml
        public static void SaveToXml(CustomerDC customer, CarDC car, List<ParamDC> param, List<SignalDC> signal) {
            XmlNode node = GetRootNode();
            XmlNode customerNode = null;
            foreach (XmlNode customerChechNode in node) {
                if (customerChechNode.Name == customer.name) {
                    customerNode = customerChechNode;
                    break;
                }
            }

            if (customerNode == null)
                customerNode = node.AppendChild(XD.CreateElement(customer.name));

            // Checking if car version already exists
            foreach (XmlNode carNode in customerNode) {
                if (carNode.Name == car.name) {
                    if (carNode.Attributes["version"] != null && ToDateTime(carNode.Attributes["version"].Value) == car.version) {
                        if (MessageBox.Show("Car already exists.\nOverride existing car?", "Conflict", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                            customerNode.RemoveChild(carNode);
                        else
                            return;
                    }
                }
            }

            XmlNode newCar = customerNode.AppendChild(XD.CreateElement(car.name));
            XmlAttribute newVersion = newCar.Attributes.Append(XD.CreateAttribute("version"));
            newVersion.InnerText = car.version.ToString(VERSION_FORMAT);

            XmlNode newParameters = newCar.AppendChild(XD.CreateElement("Parameters"));
            String _subPath = param[0].subPath;
            XmlNode subPathNode = newParameters.AppendChild(XD.CreateElement(_subPath));
            foreach (var p in param) {
                if (p.subPath != _subPath) {
                    _subPath = p.subPath;
                    subPathNode = newParameters.AppendChild(XD.CreateElement(_subPath));
                }
                XmlNode paramAtt = subPathNode.AppendChild(XD.CreateElement(p.path));
                paramAtt.InnerText = p.value.ToString();
            }

            XmlNode newSignals = newCar.AppendChild(XD.CreateElement("Signals"));
            foreach (var p in signal) {
                XmlNode newSignal = newSignals.AppendChild(XD.CreateElement(p.type));
                XmlNode typeAtt = newSignal.AppendChild(XD.CreateElement("type"));
                typeAtt.InnerText = p.type;
                XmlNode idAtt = newSignal.AppendChild(XD.CreateElement("id"));
                idAtt.InnerText = p.id.ToString();
                XmlNode startbitAtt = newSignal.AppendChild(XD.CreateElement("startbit"));
                startbitAtt.InnerText = p.startbit.ToString();
                XmlNode sizeAtt = newSignal.AppendChild(XD.CreateElement("size"));
                sizeAtt.InnerText = p.size.ToString();
                XmlNode formatAtt = newSignal.AppendChild(XD.CreateElement("format"));
                formatAtt.InnerText = p.format == "I" ? "intel" : "motorola";
                XmlNode signedAtt = newSignal.AppendChild(XD.CreateElement("signed"));
                signedAtt.InnerText = p.signed == "S" ? "1" : "0";
                XmlNode factorAtt = newSignal.AppendChild(XD.CreateElement("factor"));
                factorAtt.InnerText = p.factor.ToString();
                XmlNode offsetAtt = newSignal.AppendChild(XD.CreateElement("offset"));
                offsetAtt.InnerText = p.offset.ToString();
                XmlNode minAtt = newSignal.AppendChild(XD.CreateElement("min"));
                minAtt.InnerText = p.min.ToString();
                XmlNode maxAtt = newSignal.AppendChild(XD.CreateElement("max"));
                maxAtt.InnerText = p.max.ToString();
                XmlNode matchAtt = newSignal.AppendChild(XD.CreateElement("match"));
                matchAtt.InnerText = p.match.ToString();
            }

            XD.Save(FILE_PATH);
        }

        public static void UpdateCar() {
            XD.Save(FILE_PATH);
        }
        #endregion

        public static bool DeleteCustomer(String customerName) {
            foreach (XmlNode customerNode in ROOT_NODE) {
                if (customerNode.Name == customerName) {
                    ROOT_NODE.RemoveChild(customerNode);
                    UpdateCar();
                    return true;
                }
            }
            return false;
        }

        public static bool DeleteCar(String customerName, String carName, DateTime version) {
            foreach (XmlNode customerNode in ROOT_NODE) {
                if (customerNode.Name == customerName) {
                    foreach (XmlNode carNode in customerNode) {
                        if (carNode.Name == carName && carNode.Attributes["version"] != null && carNode.Attributes["version"].Value == version.ToString(VERSION_FORMAT)) {
                            customerNode.RemoveChild(carNode);
                            UpdateCar();
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public static DateTime ToDateTime(String version) {
            int yyyy = Convert.ToInt16(version.Substring(0, 4));
            int MM = Convert.ToInt16(version.Substring(5, 2));
            int dd = Convert.ToInt16(version.Substring(8, 2));
            int hh = Convert.ToInt16(version.Substring(11, 2));
            int mm = Convert.ToInt16(version.Substring(14, 2));
            int ss = Convert.ToInt16(version.Substring(17, 2));
            int fff = version.Length > 19 ? Convert.ToInt16(version.Substring(20, 3)) : 0;
            return new DateTime(yyyy, MM, dd, hh, mm, ss, fff);
        }

        public static bool SaveToDatabase(ECTunes.Database.ConfigLibrary.IDbConnectorRemote db, String customerName, String carName, DateTime version) {
            foreach (XmlNode customerNode in ROOT_NODE) { // Customer
                if (customerNode.Name == customerName) {
                    int _customerId = db.GetCustomerId(customerName);

                    foreach (XmlNode carNode in customerNode) {
                        if (carNode.Name == carName) {

                            bool store = false;
                            if (version == DateTime.MinValue && carNode.Attributes["version"] == null)
                                store = true;
                            else if (carNode.Attributes["version"] != null && carNode.Attributes["version"].Value == version.ToString(VERSION_FORMAT)) {
                                if (db.GetCarId(_customerId, carName, version) > 0)
                                    if (MessageBox.Show("Car already exists in this version.\nCreate new?", "Conflict", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
                                        return false;
                                store = true;
                            }

                            if (store) {

                                #region ParameterGroup
                                List<ParamDC> paramList = new List<ParamDC>();
                                List<SignalDC> signalList = new List<SignalDC>();
                                foreach (XmlNode paramTypeNode in carNode.ChildNodes) {

                                    if (paramTypeNode.Name.Equals("Parameters")) {
                                        paramList = CreateParamList(paramList, paramTypeNode.FirstChild);
                                    }

                                    if (paramTypeNode.Name.Equals("Signals")) {
                                        signalList = CreateSignalList(paramTypeNode);
                                    }
                                }
                                #endregion
                                DateTime d = db.AddToDatabase(customerName, carName, paramList, signalList);
                                if (carNode.Attributes.Count == 0)
                                    carNode.Attributes.Append(XD.CreateAttribute("version"));
                                carNode.Attributes["version"].Value = d.ToString(VERSION_FORMAT);
                                Util.XmlParser.UpdateCar();
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Recursive method returning a list of parameters
        /// </summary>
        /// <param name="paramList"></param>
        /// <param name="paramGroupNode"></param>
        /// <returns></returns>
        private static List<ParamDC> CreateParamList(List<ParamDC> paramList, XmlNode paramGroupNode) {
            if (paramGroupNode == null)
                return paramList;
            CreateParam(paramList, paramGroupNode.FirstChild, paramGroupNode.Name);
            return CreateParamList(paramList, paramGroupNode.NextSibling);
        }

        private static void CreateParam(List<ParamDC> paramList, XmlNode paramNode, String subPath) {
            if (paramNode == null)
                return;

            ParamDC param = new ParamDC();
            param.carId = 0;
            param.subPath = subPath;
            param.path = paramNode.Name;
            param.value = Convert.ToInt32(paramNode.InnerText);  // exception handling needed?
            paramList.Add(param);
            CreateParam(paramList, paramNode.NextSibling, subPath);
            return;
        }


        /// <summary>
        /// Iterative method returning a list of parameters
        /// </summary>
        /// <param name="paramTypeNode"></param>
        /// <returns></returns>
        private static List<ParamDC> CreateParamList(XmlNode paramTypeNode) {
            List<ParamDC> paramList = new List<ParamDC>();
            foreach (XmlNode paramGroupNode in paramTypeNode.ChildNodes) { // Parameter Group
                String _subPath = paramGroupNode.Name;
                foreach (XmlNode paramNode in paramGroupNode) { // Parameters
                    ParamDC param = new ParamDC();
                    param.carId = 0;
                    param.subPath = _subPath;
                    param.path = paramNode.Name;
                    param.value = Convert.ToInt32(paramNode.InnerText);  // exception handling needed?
                    paramList.Add(param);
                }
            }
            return paramList;
        }

        /// <summary>
        /// Iterative method returning a list of signals
        /// </summary>
        /// <param name="paramTypeNode"></param>
        /// <returns></returns>
        private static List<SignalDC> CreateSignalList(XmlNode paramTypeNode) {
            List<SignalDC> signalList = new List<SignalDC>();

            int signalId = 1;
            foreach (XmlNode signalNode in paramTypeNode.ChildNodes) { // Signal
                SignalDC signal = new SignalDC();
                signal.carId = 0;
                signal.signalId = signalId++;

                foreach (XmlNode signalValueNode in signalNode) { // Each value in signal
                    String value = signalValueNode.InnerText;
                    if (value.Length > 0)
                        switch (signalValueNode.Name) {
                            case "type": signal.type = value;
                                break;
                            case "id": signal.id = Convert.ToInt32(value);
                                break;
                            case "startbit": signal.startbit = Convert.ToInt32(value);
                                break;
                            case "size": signal.size = Convert.ToInt32(value);
                                break;
                            case "format": signal.format = value[0].ToString().ToUpper();
                                break;
                            case "signed": signal.signed = value[0].Equals('0') ? "U" : "S";
                                break;
                            case "factor": signal.factor = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture.NumberFormat);
                                break;
                            case "offset": signal.offset = Convert.ToInt32(value);
                                break;
                            case "min": signal.min = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture.NumberFormat);
                                break;
                            case "max": signal.max = Convert.ToInt32(value);
                                break;
                            case "match": signal.match = Convert.ToInt32(value);
                                break;
                            default:
                                break;
                        }
                }
                signalList.Add(signal);
            }

            return signalList;
        }
    }
}
using MbnApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace QuadplayMobileProxy
{
    public class QuadplayProxy
    {
        enum ActionType
        {
            Connect,
            Disconnect,
        }

        public int ID { get; private set; }
        public string InterfaceID { get; private set; }

        static string mobileProfileTemplate;

        HttpProxyListener proxyListener;

        public QuadplayProxy(int id)
        {
            ID = id;

            proxyListener = new HttpProxyListener(IPAddress.Any, ID);
        }

        static QuadplayProxy()
        {
            mobileProfileTemplate = LoadEmbdeedResource("QuadplayMobileProxy.MobileProfileTemplate.xml");
        }

        DateTime lastChangeIPTime;
        bool interfaceConnected;

        public void Start()
        {
            ChangeIP();
            proxyListener.Start();

            new Thread(CheckForConnectionThread)
            {
                Name = string.Format("ChangeIP-{0} Connection Check", ID)
            }.Start();
        }

        void CheckForConnectionThread()
        {
            int fails = 0;

            while (true)
            {
                if (interfaceConnected)
                {
                    if (!CheckForInternetConnection())
                    {
                        ++fails;
                    }
                    else
                    {
                        fails = 0;
                    }

                    if (fails > 3)
                    {
                        ChangeIP(true);
                        Console.WriteLine("No internect connection detected. Restarting. | Proxy ID: {0}", ID);
                    }
                }

                Thread.Sleep(1000);
            }
        }

        public void ChangeIP()
        {
            ChangeIP(false);
        }

        object internalThreadLocker = new object();

        void ChangeIP(bool forced)
        {
            lock (this)
            {
                TimeSpan deltaTime = DateTime.Now - lastChangeIPTime;

                if (!forced && deltaTime < TimeSpan.FromSeconds(15))
                {
                    return;
                }

                lastChangeIPTime = DateTime.Now;

                new Thread(() =>
                    {
                        lock (internalThreadLocker)
                        {
                            try
                            {
                                Console.WriteLine("Changing IP | Proxy ID: {0}", ID);

                                proxyListener.CloseClients();
                                interfaceConnected = false;

                                try
                                {
                                    ExecuteAction(ActionType.Disconnect);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Error Disconnecting: {0}", ex.ToString());
                                }

                                Thread.Sleep(3000);

                                try
                                {
                                    ExecuteAction(ActionType.Connect);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Error Connecting: {0}", ex.ToString());
                                }

                                Thread.Sleep(5000);

                                IPAddress ip = null;
                                int tries = 0;

                                while (ip == null && tries < 10)
                                {
                                    Thread.Sleep(500);

                                    ++tries;
                                    ip = GetIP();
                                }

                                interfaceConnected = true;

                                if (ip == null)
                                {
                                    Console.WriteLine("Couldnt get interface IP! | Proxy ID: {0} | Time: {1}", ID, (int)deltaTime.TotalSeconds);
                                    ChangeIP(true);
                                }
                                else
                                {
                                    proxyListener.ChangeLocalEndPoint(new IPEndPoint(ip, 0));

                                    Console.WriteLine("IP Changed! | Proxy ID: {0} | Time: {1}", ID, (int)deltaTime.TotalSeconds);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                            }
                        }
                    })
                    {
                        Name = "ChangeIP-" + ID,
                    }.Start();
            }
        }

        class MyWebClient : WebClient
        {
            IPAddress _ipAddress;

            public MyWebClient(IPAddress ipAddress)
            {
                _ipAddress = ipAddress;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = (WebRequest)base.GetWebRequest(address);

                ((HttpWebRequest)request).ServicePoint.BindIPEndPointDelegate += (servicePoint, remoteEndPoint, retryCount) =>
                {
                    return new IPEndPoint(_ipAddress, 0);
                };

                return request;
            }
        }

        bool CheckForInternetConnection()
        {
            try
            {
                IPAddress ip = GetIP();

                if (ip == null)
                {
                    return false;
                }
                else
                {
                    using (var client = new MyWebClient(ip))
                    {
                        client.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);

                        string response = client.DownloadString("http://www.google.com");
                        return response != null;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        void ExecuteAction(ActionType action)
        {
            IMbnInterfaceManager interfaceManager = (IMbnInterfaceManager)new MbnInterfaceManager();
            IMbnInterface inf = interfaceManager.GetInterface(InterfaceID);
            IMbnSubscriberInformation subscriber = inf.GetSubscriberInformation();

            XmlDocument xml = new XmlDocument();
            xml.LoadXml(mobileProfileTemplate);

            xml["MBNProfile"]["SubscriberID"].InnerText = subscriber.SubscriberID;
            xml["MBNProfile"]["SimIccID"].InnerText = subscriber.SimIccID;

            //Console.WriteLine("Profile: " + xml.OuterXml);

            IMbnConnection conn = inf.GetConnection();

            uint requestId;

            if (action == ActionType.Connect)
            {
                conn.Connect(MBN_CONNECTION_MODE.MBN_CONNECTION_MODE_TMP_PROFILE, xml.OuterXml, out requestId);
            }
            else
            {
                conn.Disconnect(out requestId);
            }
        }

        static string LoadEmbdeedResource(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = name;

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();

                return result;
            }
        }

        public void SetToInterface(string interfaceId)
        {
            proxyListener.CloseClients();
            InterfaceID = interfaceId;
        }

        public void InvalidateInterface()
        {
            proxyListener.CloseClients();
            InterfaceID = null;
        }

        public string GetMobileNumber()
        {
            try
            {
                IMbnInterfaceManager interfaceManager = (IMbnInterfaceManager)new MbnInterfaceManager();
                IMbnInterface inf = interfaceManager.GetInterface(InterfaceID);
                IMbnSubscriberInformation subscriber = inf.GetSubscriberInformation();

                foreach (var ob in subscriber.TelephoneNumbers)
                {
                    if (ob != null)
                    {
                        return (string)ob;
                    }
                }
            }
            catch { }

            return "Unknown";
        }

        public IPAddress GetIP()
        {
            return GetInterfaceIPAddress(InterfaceID);
        }

        public static IPAddress GetInterfaceIPAddress(string interfaceId)
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Id == interfaceId)
                {
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            return ip.Address;
                        }
                    }
                }
            }
            return null;
        }
    }
}

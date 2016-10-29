using MbnApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NLog;
using TrotiNet;
using TrotiNet.Example;

namespace QuadplayMobileProxy
{
    public class QuadplayProxy
    {
        static readonly Logger logger = LogManager.GetCurrentClassLogger();

        enum ActionType
        {
            Connect,
            Disconnect,
        }

        public int ID { get; private set; }
        public string InterfaceID { get; private set; }

        static string mobileProfileTemplate;

        TcpServer proxyListener;

        public QuadplayProxy(int id)
        {
            ID = id;

            proxyListener = new TcpServer(ID, false);
        }

        static QuadplayProxy()
        {
            mobileProfileTemplate = LoadEmbdeedResource("QuadplayMobileProxy.MobileProfileTemplate.xml");
        }

        DateTime lastChangeIPTime;
        TimeSpan deltaTime;
        bool changeIP;
        bool changeIPForced;
        bool makeIPCheck;

        bool interfaceConnected;

        public void Test()
        {
            IMbnInterfaceManager interfaceManager = null;
            IMbnInterface inf = null;
            IMbnSubscriberInformation subscriber = null;

            try
            {
                interfaceManager = (IMbnInterfaceManager)new MbnInterfaceManager();
                inf = interfaceManager.GetInterface(InterfaceID);
                subscriber = inf.GetSubscriberInformation();

                uint outCode = 0;
                inf.ScanNetwork(out outCode);

                uint age = 0;
                var array = inf.GetVisibleProviders(out age);

                var provider = inf.GetHomeProvider();

                //inf.SetPreferredProviders(new MBN_PROVIDER[] { plusProvider }, out outCode);

                XmlDocument xml = new XmlDocument();
                xml.LoadXml(mobileProfileTemplate);

                xml["MBNProfile"]["SubscriberID"].InnerText = subscriber.SubscriberID;
                xml["MBNProfile"]["SimIccID"].InnerText = subscriber.SimIccID;

                //Console.WriteLine("Profile: " + xml.OuterXml);

                IMbnConnection conn = null;

                try
                {
                    conn = inf.GetConnection();

                    //MBN_ACTIVATION_STATE state;
                    //string profile;
                    //conn.GetConnectionState(out state, out profile);

                    uint requestId;
                }
                finally
                {
                    if (conn != null)
                        Marshal.FinalReleaseComObject(conn);
                }
            }
            finally
            {
                if (subscriber != null)
                    Marshal.FinalReleaseComObject(subscriber);
                if (inf != null)
                    Marshal.FinalReleaseComObject(inf);
                if (interfaceManager != null)
                    Marshal.FinalReleaseComObject(interfaceManager);
            }
        }

        public void Start()
        {
            //Test();

            ChangeIP();
            proxyListener.Start(CreateProxy);

            //new Thread(CheckForConnectionThread)
            //{
            //    Name = string.Format("ChangeIP-{0} Connection Check", ID)
            //}.Start();

            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (changeIP || changeIPForced)
                        {
                            changeIP = false;
                            changeIPForced = false;

                            DoProxyChange();

                            changeIP = false;
                            changeIPForced = false;
                        }
                        else
                        {
                            Thread.Sleep(100);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error(e);
                    }
                }
            })
            { Name = "ProxyWorkThread-" + ID }.Start();

            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (makeIPCheck)
                        {
                            makeIPCheck = false;
                            Console.WriteLine("Doing IP Check. Proxy: {0}", ID);
                            DoIPCheck();
                        }
                        else
                        {
                            Thread.Sleep(100);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error(e);
                    }
                }
            })
            { Name = "ProxyIPCheck-" + ID }.Start();

            if (Program.IPRangeTest)
            {
                new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            ChangeIP(true);

                            Thread.Sleep(1000 * 30);
                        }
                        catch (Exception e)
                        {
                            logger.Error(e);
                        }
                    }
                })
                { Name = "ProxyIPRange-" + ID }.Start();
            }
        }

        void DoProxyChange()
        {
            logger.Debug($"Changing IP, Proxy ID: {ID}");

            proxyListener.IsPaused = true;

            proxyListener.CloseAllSockets();
            //proxyListener.CloseClients();
            interfaceConnected = false;

            try
            {
                ExecuteAction(ActionType.Disconnect);
            }
            catch (Exception ex)
            {
                //Console.WriteLine("Error Disconnecting: {0}", ex.ToString());
                logger.Debug($"Error Disconnecting, Proxy ID: {ID}");
            }

            Thread.Sleep(10000);

            try
            {
                ExecuteAction(ActionType.Connect);
            }
            catch (Exception ex)
            {
                logger.Debug($"Error Connecting: {ex}");
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
                logger.Debug($"Couldnt get interface IP! Proxy ID: {ID}, Time: '{(int) deltaTime.TotalSeconds}'");
                ChangeIP(true);
            }
            else
            {
                //proxyListener.ChangeLocalEndPoint(new IPEndPoint(ip, 0));
                proxyListener.CloseAllSockets();

                proxyListener.IsPaused = false;
                //proxyListener.ReconnectAllSockets(ip);

                logger.Debug($"IP Changed! Proxy ID: {ID}, Time: '{(int) deltaTime.TotalSeconds}', Bind: '{ip}'");

                //new Thread(() =>
                //{
                //    DoIPCheck();
                //}).Start();

                makeIPCheck = true;
            }
        }

        public TransparentProxy CreateProxy(HttpSocket clientSocket)
        {
            var proxy = new TransparentProxy(clientSocket, this);

            IPAddress ip = GetIP();
            int tries = 0;
            while (ip == null && tries < 5)
            {
                Thread.Sleep(500);

                ++tries;
                ip = GetIP();
            }

            if (ip != null)
            {
                proxy.InterfaceToBind = ip;
                return proxy;
            }

            return null;
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
                deltaTime = DateTime.Now - lastChangeIPTime;

                if (!forced && deltaTime < TimeSpan.FromSeconds(30))
                    return;

                lastChangeIPTime = DateTime.Now;
                changeIP = true;
            }
        }

        List<string> lastIpList = new List<string>();

        static object ipLogLocker = new object();

        int nextIpCheckWaitTime = 20;

        void DoIPCheck()
        {
            for (int n = 0; n < nextIpCheckWaitTime; ++n)
            {
                string ip;
                if (CheckForInternetConnection(out ip))
                {
                    if (ip != null)
                    {
                        lock (ipLogLocker)
                        {
                            File.AppendAllLines("IP_Log.txt", new string[] { ip });
                        }

                        if (lastIpList.Contains(ip))
                        {
                            logger.Warn($"Current IP already seen. Changing. Proxy: {ID} : IP: {ip}");
                            changeIPForced = true;
                            return;
                        }
                        else
                        {
                            logger.Info($"New IP looks good. Proxy: {ID} : IP: {ip}");

                            lastIpList.Add(ip);
                            while (lastIpList.Count > 5)
                            {
                                lastIpList.RemoveAt(0);
                            }

                            nextIpCheckWaitTime = 20;

                            return;
                        }
                    }
                }

                nextIpCheckWaitTime += 3;
                logger.Debug($"No Connection Detected. Proxy: {ID}");
                Thread.Sleep(1000);
            }

            logger.Warn($"Too many failed connections. Changing. Proxy: {ID}");
            changeIPForced = true;
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

        bool CheckForInternetConnection(out string externalIP)
        {
            externalIP = null;

            try
            {
                IPAddress ip = GetIP();

                if (ip == null)
                {
                    return false;
                }
                else
                {
                    string detectedIp = null;
                    bool successDetected = false;

                    var httpThread = new Thread(() =>
                    {
                        try
                        {
                            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://checkip.dyndns.org");
                            request.ServicePoint.BindIPEndPointDelegate = new BindIPEndPoint((ServicePoint servicePoint, IPEndPoint remoteEndPoint, int retryCount) =>
                                {
                                    logger.Trace($"BindIPEndpoint called. Binding to: {ip}, Retry: {retryCount}");

                                    if (retryCount > 3)
                                        Thread.Sleep(500);

                                    return new IPEndPoint(ip, 0);
                                });
                            request.Timeout = 5000;
                            request.ContinueTimeout = 5000;
                            request.ReadWriteTimeout = 5000;
                            request.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);

                            using (var stream = request.GetResponse().GetResponseStream())
                            using (StreamReader sr = new StreamReader(stream))
                            {
                                string response = sr.ReadToEnd().Trim();

                                logger.Trace($"Got chceck response. Proxy: {ID}, Response: {response}");

                                string[] a = response.Split(':');
                                string a2 = a[1].Substring(1);
                                string[] a3 = a2.Split('<');
                                string a4 = a3[0];

                                detectedIp = a4;
                                successDetected = true;
                            }
                        }
                        catch (ThreadAbortException)
                        {
                            logger.Trace($"Aborting connection check. Proxy: {ID}");
                        }
                        catch (Exception e)
                        {
                            logger.Warn(e);
                        }
                    });

                    httpThread.Start();
                    httpThread.Join(TimeSpan.FromSeconds(8));

                    try
                    {
                        if (httpThread.IsAlive)
                            httpThread.Abort();
                    }
                    catch (Exception e)
                    {
                        logger.Warn(e);
                    }

                    externalIP = detectedIp;
                    return successDetected;
                }
            }
            catch
            {
                return false;
            }
        }

        void ExecuteAction(ActionType action)
        {
            IMbnInterfaceManager interfaceManager = null;
            IMbnInterface inf = null;
            IMbnSubscriberInformation subscriber = null;

            try
            {
                interfaceManager = (IMbnInterfaceManager)new MbnInterfaceManager();
                inf = interfaceManager.GetInterface(InterfaceID);
                subscriber = inf.GetSubscriberInformation();

                XmlDocument xml = new XmlDocument();
                xml.LoadXml(mobileProfileTemplate);

                xml["MBNProfile"]["SubscriberID"].InnerText = subscriber.SubscriberID;
                xml["MBNProfile"]["SimIccID"].InnerText = subscriber.SimIccID;

                //Console.WriteLine("Profile: " + xml.OuterXml);

                IMbnConnection conn = null;

                try
                {
                    conn = inf.GetConnection();

                    //MBN_ACTIVATION_STATE state;
                    //string profile;
                    //conn.GetConnectionState(out state, out profile);

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
                finally
                {
                    if (conn != null)
                        Marshal.FinalReleaseComObject(conn);
                }
            }
            finally
            {
                if (subscriber != null)
                    Marshal.FinalReleaseComObject(subscriber);
                if (inf != null)
                    Marshal.FinalReleaseComObject(inf);
                if (interfaceManager != null)
                    Marshal.FinalReleaseComObject(interfaceManager);
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
            proxyListener.CloseAllSockets();
            InterfaceID = interfaceId;
        }

        public void InvalidateInterface()
        {
            proxyListener.CloseAllSockets();
            InterfaceID = null;
        }

        public string GetMobileNumber()
        {
            IMbnInterfaceManager interfaceManager = null;
            IMbnInterface inf = null;
            IMbnSubscriberInformation subscriber = null;

            try
            {
                interfaceManager = (IMbnInterfaceManager) new MbnInterfaceManager();
                inf = interfaceManager.GetInterface(InterfaceID);
                subscriber = inf.GetSubscriberInformation();

                foreach (var ob in subscriber.TelephoneNumbers)
                {
                    if (ob != null)
                    {
                        return (string) ob;
                    }
                }
            }
            catch (Exception e)
            {
                logger.Warn(e);
            }
            finally
            {
                if (subscriber != null)
                    Marshal.FinalReleaseComObject(subscriber);
                if (inf != null)
                    Marshal.FinalReleaseComObject(inf);
                if (interfaceManager != null)
                    Marshal.FinalReleaseComObject(interfaceManager);
            }

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

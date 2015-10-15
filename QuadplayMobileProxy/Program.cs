using MbnApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuadplayMobileProxy
{
    public class Program
    {
        //public static IMbnInterfaceManager InterfaceManager { get; private set; }

        static List<int> proxiesToChangeIP = new List<int>();
        static List<QuadplayProxy> quadplayProxyList = new List<QuadplayProxy>();

        static void Main(string[] args)
        {
            Console.SetOut(new CustomTextWriter(Console.Out));

            IMbnInterfaceManager interfaceManager = (IMbnInterfaceManager)new MbnInterfaceManager();

            //InterfaceManager = interfaceManager;

            int staringPort = Int32.Parse(args[0]);
            Console.WriteLine("Listening start port: {0}", staringPort);

            int proxyCount = Int32.Parse(args[1]);
            Console.WriteLine("Starting {0} Proxies", proxyCount);

            int nextID = staringPort + 1;

            for (int n = 0; n < proxyCount; ++n)
            {
                quadplayProxyList.Add(new QuadplayProxy(nextID++));
            }

            RefreshRunningProxies();

            foreach (var qproxy in quadplayProxyList)
            {
                qproxy.Start();
            }

            RunServer(staringPort);

            new Thread(() =>
                {
                    while (true)
                    {
                        RefreshRunningProxies();

                        Thread.Sleep(5000);
                    }
                })
            {
                Name = "RefreshInterfaces",
            }.Start();

            while (true)
            {
                lock (proxiesToChangeIP)
                {
                    for (int n = proxiesToChangeIP.Count - 1; n >= 0; --n)
                    {
                        QuadplayProxy qproxy = null;

                        foreach (var qp in quadplayProxyList)
                        {
                            if (qp.ID == proxiesToChangeIP[n])
                            {
                                qproxy = qp;
                                break;
                            }
                        }

                        proxiesToChangeIP.RemoveAt(n);

                        qproxy.ChangeIP();
                    }
                }

                Thread.Sleep(50);
            }
        }

        static void RefreshRunningProxies()
        {
            List<string> infIdList = new List<string>();

            IMbnInterfaceManager interfaceManager = (IMbnInterfaceManager)new MbnInterfaceManager();

            foreach (var obj in interfaceManager.GetInterfaces())
            {
                IMbnInterface inf = (IMbnInterface)obj;
                infIdList.Add(inf.InterfaceID);
            }

            foreach (var qproxy in quadplayProxyList)
            {
                if (!infIdList.Contains(qproxy.InterfaceID))
                {
                    qproxy.InvalidateInterface();
                }
            }

            foreach (string infId in infIdList)
            {
                bool exists = false;

                foreach (var qproxy in quadplayProxyList)
                {
                    if (qproxy.InterfaceID == infId)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    foreach (var qproxy in quadplayProxyList)
                    {
                        if (qproxy.InterfaceID == null)
                        {
                            qproxy.SetToInterface(infId);

                            Console.WriteLine("Binded Proxy: {0} To Interface: {1}", qproxy.ID, qproxy.InterfaceID);

                            break;
                        }
                    }
                }
            }
        }

        static void RunServer(int port)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(String.Format("http://*:{0}/", port));
            listener.Start();

            Console.WriteLine("Server listening on port: {0}", port);

            new Thread(() =>
            {
                while (listener.IsListening)
                {
                    try
                    {
                        HttpListenerContext context = listener.GetContext();

                        HttpListenerRequest request = context.Request;
                        HttpListenerResponse response = context.Response;

                        string responseString;

                        //Console.WriteLine("Received request: {0}", request.RawUrl);

                        if (request.RawUrl.StartsWith("/changeip"))
                        {
                            string[] splitted = request.RawUrl.Split('?');
                            int proxyId = Int32.Parse(splitted[1]);

                            //Console.WriteLine("Received request to change ip. Proxy ID: {0}", proxyId);

                            lock (proxiesToChangeIP)
                            {
                                proxiesToChangeIP.Add(proxyId);
                            }

                            string number = quadplayProxyList.Where(el => el.ID == proxyId).FirstOrDefault().GetMobileNumber();

                            responseString = "OK;10000;" + number;
                        }
                        else if (request.RawUrl.StartsWith("/getmobilenumber"))
                        {
                            string[] splitted = request.RawUrl.Split('?');
                            int proxyId = Int32.Parse(splitted[1]);

                            string number = quadplayProxyList.Where(el => el.ID == proxyId).FirstOrDefault().GetMobileNumber();

                            responseString = "OK;" + number;
                        }
                        else if (request.RawUrl == "/proxylist")
                        {
                            //Console.WriteLine("Received request to get proxies");

                            StringWriter stringWriter = new StringWriter();
                            stringWriter.Write("AVAIL");

                            foreach (var qproxy in quadplayProxyList)
                            {
                                stringWriter.Write(";{0}", qproxy.ID);
                            }

                            responseString = stringWriter.ToString();
                        }
                        else
                        {
                            Console.WriteLine("Received bad request: {0}", request.RawUrl);

                            responseString = "WRONG_URL";
                        }

                        response.StatusCode = 200;

                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                        response.ContentLength64 = buffer.Length;

                        response.OutputStream.Write(buffer, 0, buffer.Length);
                        response.OutputStream.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                    }
                }

                listener.Stop();
                listener.Close();
            })
            {
                Name = "ServerListener",
            }.Start();
        }

        class CustomTextWriter : TextWriter
        {
            public override Encoding Encoding
            {
                get { return original.Encoding; }
            }

            TextWriter original;

            public CustomTextWriter(TextWriter original)
            {
                this.original = original;
            }

            public override void WriteLine(string value)
            {
                original.WriteLine("[{0}] > {1}", DateTime.Now.ToString("HH:mm:ss:fff"), value);
            }
        }
    }
}

/*
 * This file is part of the tutorial on how to use the TrotiNet library.
 *
 * Here, we show how to derive a transparent proxy from the base class
 * ProxyLogic which implements all the necessary logic related to the 
 * HTTP protocol.
 *
 * TransparentProxy is a proxy that does not change the semantics of
 * the communication, but simply logs requests and answers. The purpose
 * of this example is to show the two callbacks OnReceiveRequest()
 * and OnReceiveResponse(), which are called by the base class ProxyLogic.
 */
using QuadplayMobileProxy;
using System;

namespace TrotiNet.Example
{
    public class TransparentProxy : ProxyLogic
    {
        QuadplayProxy quadplayProxy;

        public TransparentProxy(HttpSocket clientSocket, QuadplayProxy quadplayProxy)
            : base(clientSocket)
        {
            this.quadplayProxy = quadplayProxy;
        }

        static new public TransparentProxy CreateProxy(HttpSocket clientSocket, QuadplayProxy quadplayProxy)
        {
            return new TransparentProxy(clientSocket, quadplayProxy);
        }

        protected override void OnReceiveRequest()
        {
            if (RequestLine.RequestLine.Contains("quadplayproxy.internal/changeip"))
            {
                Console.WriteLine("Got Internal ChangeIP Command! Proxy ID: " + quadplayProxy.ID);
                quadplayProxy.ChangeIP();

                this.SocketBPClient.WriteAsciiLine(string.Format("HTTP/{0} 200 IP Changed", RequestLine.ProtocolVersion));
                this.SocketBPClient.WriteAsciiLine(string.Empty);

                AbortRequest();

                throw new Exception("Abort Request");
            }

            Console.WriteLine("-> " + RequestLine + " from HTTP referer " + RequestHeaders.Referer);
        }

        protected override void OnReceiveResponse()
        {
            Console.WriteLine("<- " + ResponseStatusLine + " with HTTP Content-Length: " + (ResponseHeaders.ContentLength ?? 0));
        }
    }
}
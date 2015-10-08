/*
    Copyright © 2002, The KPD-Team
    All rights reserved.
    http://www.mentalis.org/

  Redistribution and use in source and binary forms, with or without
  modification, are permitted provided that the following conditions
  are met:

    - Redistributions of source code must retain the above copyright
       notice, this list of conditions and the following disclaimer. 

    - Neither the name of the KPD-Team, nor the names of its contributors
       may be used to endorse or promote products derived from this
       software without specific prior written permission. 

  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
  "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
  LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
  FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL
  THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
  (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
  SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
  STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
  ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
  OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace QuadplayMobileProxy
{

    /// <summary>References the callback method to be called when the <c>Client</c> object disconnects from the local client and the remote server.</summary>
    /// <param name="client">The <c>Client</c> that has closed its connections.</param>
    public delegate void DestroyDelegate(ProxyClient client);

    ///<summary>Specifies the basic methods and properties of a <c>Client</c> object. This is an abstract class and must be inherited.</summary>
    ///<remarks>The Client class provides an abstract base class that represents a connection to a local client and a remote server. Descendant classes further specify the protocol that is used between those two connections.</remarks>
    public abstract class ProxyClient : IDisposable
    {
        ///<summary>Initializes a new instance of the Client class.</summary>
        ///<param name="ClientSocket">The <see cref ="Socket">Socket</see> connection between this proxy server and the local client.</param>
        ///<param name="Destroyer">The callback method to be called when this Client object disconnects from the local client and the remote server.</param>
        public ProxyClient(Socket ClientSocket, DestroyDelegate Destroyer)
        {
            this.ClientSocket = ClientSocket;
            this.Destroyer = Destroyer;
        }
        ///<summary>Initializes a new instance of the Client object.</summary>
        ///<remarks>Both the ClientSocket property and the DestroyDelegate are initialized to null.</remarks>
        public ProxyClient()
        {
            this.ClientSocket = null;
            this.Destroyer = null;
        }
        ///<summary>Gets or sets the Socket connection between the proxy server and the local client.</summary>
        ///<value>A Socket instance defining the connection between the proxy server and the local client.</value>
        ///<seealso cref ="DestinationSocket"/>
        internal Socket ClientSocket
        {
            get
            {
                return m_ClientSocket;
            }
            set
            {
                if (m_ClientSocket != null)
                    m_ClientSocket.Close();
                m_ClientSocket = value;
            }
        }
        ///<summary>Gets or sets the Socket connection between the proxy server and the remote host.</summary>
        ///<value>A Socket instance defining the connection between the proxy server and the remote host.</value>
        ///<seealso cref ="ClientSocket"/>
        internal Socket DestinationSocket
        {
            get
            {
                return m_DestinationSocket;
            }
            set
            {
                if (m_DestinationSocket != null)
                    m_DestinationSocket.Close();
                m_DestinationSocket = value;
            }
        }
        ///<summary>Gets the buffer to store all the incoming data from the local client.</summary>
        ///<value>An array of bytes that can be used to store all the incoming data from the local client.</value>
        ///<seealso cref ="RemoteBuffer"/>
        protected byte[] Buffer
        {
            get
            {
                return m_Buffer;
            }
        }
        ///<summary>Gets the buffer to store all the incoming data from the remote host.</summary>
        ///<value>An array of bytes that can be used to store all the incoming data from the remote host.</value>
        ///<seealso cref ="Buffer"/>
        protected byte[] RemoteBuffer
        {
            get
            {
                return m_RemoteBuffer;
            }
        }
        ///<summary>Disposes of the resources (other than memory) used by the Client.</summary>
        ///<remarks>Closes the connections with the local client and the remote host. Once <c>Dispose</c> has been called, this object should not be used anymore.</remarks>
        ///<seealso cref ="System.IDisposable"/>
        public void Dispose()
        {
            lock (this)
            {
                //Console.WriteLine("Client disposing");

                try
                {
                    ClientSocket.Shutdown(SocketShutdown.Both);
                }
                catch { }
                try
                {
                    DestinationSocket.Shutdown(SocketShutdown.Both);
                }
                catch { }
                //Close the sockets
                if (ClientSocket != null)
                    ClientSocket.Close();
                if (DestinationSocket != null)
                    DestinationSocket.Close();
                //Clean up
                ClientSocket = null;
                DestinationSocket = null;
                if (Destroyer != null)
                    Destroyer(this);
            }
        }
        ///<summary>Returns text information about this Client object.</summary>
        ///<returns>A string representing this Client object.</returns>
        public override string ToString()
        {
            try
            {
                return "Incoming connection from " + ((IPEndPoint)DestinationSocket.RemoteEndPoint).Address.ToString();
            }
            catch
            {
                return "Client connection";
            }
        }
        ///<summary>Starts relaying data between the remote host and the local client.</summary>
        ///<remarks>This method should only be called after all protocol specific communication has been finished.</remarks>
        public void StartRelay()
        {
            try
            {
                ClientSocket.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, new AsyncCallback(this.OnClientReceive), ClientSocket);
                DestinationSocket.BeginReceive(RemoteBuffer, 0, RemoteBuffer.Length, SocketFlags.None, new AsyncCallback(this.OnRemoteReceive), DestinationSocket);
            }
            catch
            {
                Dispose();
            }
        }
        ///<summary>Called when we have received data from the local client.<br>Incoming data will immediately be forwarded to the remote host.</br></summary>
        ///<param name="ar">The result of the asynchronous operation.</param>
        protected void OnClientReceive(IAsyncResult ar)
        {
            try
            {
                int Ret = ClientSocket.EndReceive(ar);
                if (Ret <= 0)
                {
                    Dispose();
                    return;
                }

                //WriteSSLPacketInfo(Buffer, Ret, true);

                //Ret = ProccessDataToSend(Buffer, Ret);

                DestinationSocket.BeginSend(Buffer, 0, Ret, SocketFlags.None, new AsyncCallback(this.OnRemoteSent), DestinationSocket);
            }
            catch
            {
                Dispose();
            }
        }

        private void WriteSSLPacketInfo(byte[] buffer, int size, bool sent)
        {
            byte[] data = new byte[size];

            System.Buffer.BlockCopy(Buffer, 0, data, 0, size);

            if (data.Length >= 6)
            {
                byte messageType = data[0];
                byte handshakeType = data[5];

                byte versionMajor = data[1];
                byte versionMinor = data[2];

                if (messageType == 0x16)
                {
                    string hash;

                    using (SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
                    {
                        hash = Convert.ToBase64String(sha1.ComputeHash(data));
                    }

                    Console.WriteLine("SSL Message! Status: {0} | Type: {1} | Version: {2}.{3} | Hash: {4}",
                        sent ? "Sent" : "Received", handshakeType, versionMajor, versionMinor, hash);

                    //Buffer[1] = 3;
                    //Buffer[2] = 1;
                }

                return;

                if (messageType == 0x16 && handshakeType == 0x01)
                {
                    short recordLength = BitConverter.ToInt16(new byte[] { data[4], data[3] }, 0);

                    Console.WriteLine("SSL ClientHello! Version: {0}.{1} | Record Length: {2} / {3}",
                        versionMajor, versionMinor, recordLength, data.Length);

                    using (BinaryReader reader = new BinaryReader(new MemoryStream(data, 6, data.Length - 6)))
                    {
                        int messageLength = ReadIntFromReader(reader, 3);

                        byte versionMajorTwo = reader.ReadByte();
                        byte versionMinorTwo = reader.ReadByte();

                        byte[] randomNumber = reader.ReadBytes(32);

                        int sessionIdLength = ReadIntFromReader(reader, 1);
                        byte[] sessionId = reader.ReadBytes(sessionIdLength);

                        int cipherSuitesLength = ReadIntFromReader(reader, 2);
                        byte[] cipherSuites = reader.ReadBytes(cipherSuitesLength);

                        int compressionMethodsLength = ReadIntFromReader(reader, 1);
                        byte[] compressionMethods = reader.ReadBytes(compressionMethodsLength);

                        int extensionsLength = ReadIntFromReader(reader, 2);
                        byte[] extensions = reader.ReadBytes(extensionsLength);

                        Console.WriteLine("Message Length: {0} | Version: {1}.{2} | SessionID Length: {3} | Cipher Suites Length: {4} | Compression Methods Length: {5} | Extensions Length: {6}",
                            messageLength, versionMajorTwo, versionMinorTwo, sessionIdLength, cipherSuitesLength, compressionMethodsLength, extensionsLength / 4);
                    }
                }

                Console.WriteLine();
                Console.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
                Console.WriteLine();
            }
        }

        private int ProccessDataToSend(byte[] buffer, int size)
        {
            byte[] data = new byte[size];

            System.Buffer.BlockCopy(buffer, 0, data, 0, size);

            if (data.Length >= 6)
            {
                byte messageType = data[0];
                byte handshakeType = data[5];

                byte versionMajor = data[1];
                byte versionMinor = data[2];

                if (messageType == 0x16)
                {
                    //Console.WriteLine("SSL Message! Type: {0} | Version: {1}.{2}",
                    //   handshakeType, versionMajor, versionMinor);

                    //Buffer[1] = 3;
                    //Buffer[2] = 1;
                }

                if (messageType == 0x16 && handshakeType == 0x01)
                {
                    short recordLength = BitConverter.ToInt16(new byte[] { data[4], data[3] }, 0);

                    //Console.WriteLine("SSL ClientHello! Version: {0}.{1} | Record Length: {2} / {3}",
                    //    versionMajor, versionMinor, recordLength, data.Length);

                    using (BinaryReader reader = new BinaryReader(new MemoryStream(data, 6, data.Length - 6)))
                    {
                        int messageLength = ReadIntFromReader(reader, 3);

                        byte versionMajorTwo = reader.ReadByte();
                        byte versionMinorTwo = reader.ReadByte();

                        byte[] randomNumber = reader.ReadBytes(32);

                        int sessionIdLength = ReadIntFromReader(reader, 1);
                        byte[] sessionId = reader.ReadBytes(sessionIdLength);

                        int cipherSuitesLength = ReadIntFromReader(reader, 2);
                        byte[] cipherSuites = reader.ReadBytes(cipherSuitesLength);

                        int compressionMethodsLength = ReadIntFromReader(reader, 1);
                        byte[] compressionMethods = reader.ReadBytes(compressionMethodsLength);

                        int extensionsLength = ReadIntFromReader(reader, 2);
                        byte[] extensions = reader.ReadBytes(extensionsLength);

                        //Console.WriteLine("Message Length: {0} | Version: {1}.{2} | SessionID Length: {3} | Cipher Suites Length: {4} | Compression Methods Length: {5} | Extensions Length: {6}",
                        //    messageLength, versionMajorTwo, versionMinorTwo, sessionIdLength, cipherSuitesLength, compressionMethodsLength, extensionsLength / 4);

                        //Console.WriteLine();
                        //Console.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
                        //Console.WriteLine();

                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            using (BinaryWriter writer = new BinaryWriter(memoryStream))
                            {
                                memoryStream.Position = 9;

                                writer.Write((byte)3);
                                writer.Write((byte)1);

                                writer.Write(randomNumber, 0, randomNumber.Length);

                                WriteIntToWriter(sessionIdLength, writer, 1);
                                writer.Write(sessionId, 0, sessionId.Length);

                                byte[] myCipherSuites = Convert.FromBase64String("AAQABQAvADXAAsAEwAXADMAOwA/AB8AJwArAEcATwBQAMwA5ADIAOAAKwAPADcAIwBIAFgATAAkAFQASAAMACAAUABEA/w==");
                                WriteIntToWriter(myCipherSuites.Length, writer, 2);
                                writer.Write(myCipherSuites, 0, myCipherSuites.Length);

                                byte[] myCompressionMethods = Convert.FromBase64String("AA==");
                                WriteIntToWriter(myCompressionMethods.Length, writer, 1);
                                writer.Write(myCompressionMethods, 0, myCompressionMethods.Length);

                                byte[] myExtensions = Convert.FromBase64String("AAsABAMAAQIACgA0ADIADgANABkACwAMABgACQAKABYAFwAIAAYABwAUABUABAAFABIAEwABAAIAAwAPABAAEQ==");
                                WriteIntToWriter(myExtensions.Length, writer, 2);
                                writer.Write(myExtensions, 0, myExtensions.Length);

                                int messageLengthToWrite = (int)(memoryStream.Position - 9);
                                memoryStream.Position = 6;
                                WriteIntToWriter(messageLengthToWrite, writer, 3);

                                memoryStream.Position = 0;

                                writer.Write((byte)0x16);
                                writer.Write((byte)3);
                                writer.Write((byte)1);

                                int recordLengthToWrite = messageLengthToWrite + 4;
                                WriteIntToWriter(recordLengthToWrite, writer, 2);

                                writer.Write((byte)0x01);
                            }

                            byte[] messageByteArray = memoryStream.ToArray();
                            System.Buffer.BlockCopy(messageByteArray, 0, buffer, 0, messageByteArray.Length);

                            return messageByteArray.Length;
                        }
                    }
                }
            }

            return size;
        }

        private void ProccessDataToSendA(int size)
        {
            byte[] data = new byte[size];

            System.Buffer.BlockCopy(Buffer, 0, data, 0, size);

            if (data.Length >= 6)
            {
                byte messageType = data[0];
                byte handshakeType = data[5];

                if (messageType == 0x16 && handshakeType == 0x01)
                {
                    byte versionMajor = data[1];
                    byte versionMinor = data[2];

                    short recordLength = BitConverter.ToInt16(new byte[] { data[4], data[3] }, 0);

                    StringWriter stringWriter = new StringWriter();

                    stringWriter.WriteLine("SSL ClientHello! Version: {0}.{1} | Record Length: {2} / {3}",
                        versionMajor, versionMinor, recordLength, data.Length);

                    stringWriter.WriteLine();

                    using (BinaryReader reader = new BinaryReader(new MemoryStream(data, 6, data.Length - 6)))
                    {
                        int messageLength = ReadIntFromReader(reader, 3);

                        byte versionMajorTwo = reader.ReadByte();
                        byte versionMinorTwo = reader.ReadByte();

                        byte[] randomNumber = reader.ReadBytes(32);

                        int sessionIdLength = ReadIntFromReader(reader, 1);
                        byte[] sessionId = reader.ReadBytes(sessionIdLength);

                        int cipherSuitesLength = ReadIntFromReader(reader, 2);
                        byte[] cipherSuites = reader.ReadBytes(cipherSuitesLength);

                        int compressionMethodsLength = ReadIntFromReader(reader, 1);
                        byte[] compressionMethods = reader.ReadBytes(compressionMethodsLength);

                        int extensionsLength = ReadIntFromReader(reader, 2);
                        byte[] extensions = reader.ReadBytes(extensionsLength);

                        stringWriter.WriteLine("Message Length: {0} | Version: {1}.{2} | SessionID Length: {3} | Cipher Suites Length: {4} | Compression Methods Length: {5} | Extensions Length: {6}",
                            messageLength, versionMajorTwo, versionMinorTwo, sessionIdLength, cipherSuitesLength, compressionMethodsLength, extensionsLength / 4);

                        stringWriter.WriteLine();

                        stringWriter.WriteLine("Cipher Suites: {0}", Convert.ToBase64String(cipherSuites));

                        stringWriter.WriteLine();

                        stringWriter.WriteLine("Compression Methods: {0}", Convert.ToBase64String(compressionMethods));

                        stringWriter.WriteLine();

                        stringWriter.WriteLine("Extensions: {0}", Convert.ToBase64String(extensions));

                        stringWriter.WriteLine();
                        stringWriter.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
                        stringWriter.WriteLine();

                        string stringWriterText = stringWriter.ToString();

                        Console.Write(stringWriterText);
                        File.AppendAllText("ClientHellos.txt", stringWriterText);
                    }
                }
            }
        }

        private static void WriteIntToWriter(int number, BinaryWriter writer, int byteCount)
        {
            byte[] buffer = BitConverter.GetBytes(number).Reverse().ToArray();

            writer.Write(buffer, 4 - byteCount, byteCount);
        }

        private static int ReadIntFromReader(BinaryReader reader, int byteCount)
        {
            byte[] buffer = new byte[4];
            reader.Read(buffer, 4 - byteCount, byteCount);

            buffer = buffer.Reverse().ToArray();

            return BitConverter.ToInt32(buffer, 0);
        }

        ///<summary>Called when we have sent data to the remote host.<br>When all the data has been sent, we will start receiving again from the local client.</br></summary>
        ///<param name="ar">The result of the asynchronous operation.</param>
        protected void OnRemoteSent(IAsyncResult ar)
        {
            try
            {
                int Ret = DestinationSocket.EndSend(ar);
                if (Ret > 0)
                {
                    ClientSocket.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, new AsyncCallback(this.OnClientReceive), ClientSocket);
                    return;
                }
            }
            catch { }
            Dispose();
        }
        ///<summary>Called when we have received data from the remote host.<br>Incoming data will immediately be forwarded to the local client.</br></summary>
        ///<param name="ar">The result of the asynchronous operation.</param>
        protected void OnRemoteReceive(IAsyncResult ar)
        {
            try
            {
                int Ret = DestinationSocket.EndReceive(ar);
                if (Ret <= 0)
                {
                    Dispose();
                    return;
                }

                //WriteSSLPacketInfo(RemoteBuffer, Ret, false);

                //Ret = ProccessDataToSend(RemoteBuffer, Ret);

                ClientSocket.BeginSend(RemoteBuffer, 0, Ret, SocketFlags.None, new AsyncCallback(this.OnClientSent), ClientSocket);
            }
            catch
            {
                Dispose();
            }
        }
        ///<summary>Called when we have sent data to the local client.<br>When all the data has been sent, we will start receiving again from the remote host.</br></summary>
        ///<param name="ar">The result of the asynchronous operation.</param>
        protected void OnClientSent(IAsyncResult ar)
        {
            try
            {
                int Ret = ClientSocket.EndSend(ar);
                if (Ret > 0)
                {
                    DestinationSocket.BeginReceive(RemoteBuffer, 0, RemoteBuffer.Length, SocketFlags.None, new AsyncCallback(this.OnRemoteReceive), DestinationSocket);
                    return;
                }
            }
            catch { }
            Dispose();
        }
        ///<summary>Starts communication with the local client.</summary>
        public abstract void StartHandshake();
        // private variables
        /// <summary>Holds the address of the method to call when this client is ready to be destroyed.</summary>
        private DestroyDelegate Destroyer;
        /// <summary>Holds the value of the ClientSocket property.</summary>
        private Socket m_ClientSocket;
        /// <summary>Holds the value of the DestinationSocket property.</summary>
        private Socket m_DestinationSocket;
        /// <summary>Holds the value of the Buffer property.</summary>
        private byte[] m_Buffer = new byte[1024 * 128]; //0<->4095 = 4096
        /// <summary>Holds the value of the RemoteBuffer property.</summary>
        private byte[] m_RemoteBuffer = new byte[1024 * 128];
    }

}
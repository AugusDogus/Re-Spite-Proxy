// -----------------------------------------------------------------------
// <copyright file="Server.cs">
// (c) Augie Luebbers - Re:Spite Proxy
// </copyright>
// -----------------------------------------------------------------------

namespace RespiteTcpProxy.Classes
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>
    /// TCP Proxy Client Implementation
    /// 
    /// </summary>
    public class Client
    {
        /// <summary>
        /// The maximum amount of data to receive in a single packet.
        /// </summary>
        private static Int32 MAX_BUFFER_SIZE = 2048;

        /// <summary>
        /// Internal client state to prevent multiple stop calls.
        /// (Helps reduce the number of unneeded exceptions.)
        /// </summary>
        private Boolean m_vIsRunning;

        /// <summary>
        /// Client variables.
        /// </summary>
        private Socket m_vClientSocket;
        private Byte[] m_vClientBuffer;
        private List<Byte> m_vClientBacklog;

        /// <summary>
        /// Server variables.
        /// </summary>
        private Socket m_vServerSocket;
        private Byte[] m_vServerBuffer;
        private List<Byte> m_vServerBacklog;

        /// <summary>
        /// Default Constructor
        /// </summary>
        /// <param name="sockClient"></param>
        public Client(Socket sockClient)
        {
            // Setup class defaults..
            this.m_vClientSocket = sockClient;
            this.m_vClientBuffer = new Byte[MAX_BUFFER_SIZE];
            this.m_vClientBacklog = new List<Byte>();

            this.m_vServerSocket = null;
            this.m_vServerBuffer = new Byte[MAX_BUFFER_SIZE];
            this.m_vServerBacklog = new List<Byte>();
        }

        /// <summary>
        /// Starts our proxy client.
        /// </summary>
        /// <param name="remoteTarget"></param>
        /// <param name="remotePort"></param>
        /// <returns></returns>
        public bool Start(String remoteTarget = "127.0.0.1", Int32 remotePort = 8768)
        {
  
            // Stop this client if it was already started before..
            if (this.m_vIsRunning == true)
                this.Stop();
            this.m_vIsRunning = true;

            // Attempt to parse the given remote target.
            // This allows an IP address or domain to be given.
            // Ex:
            //      127.0.0.1
            //      derp.no-ip.org

            IPAddress ipAddress = null;
            try { ipAddress = IPAddress.Parse(remoteTarget); }
            catch
            {
                try { ipAddress = Dns.GetHostEntry(remoteTarget).AddressList[0]; }
                catch { throw new SocketException((int)SocketError.HostNotFound); }
            }

            try
            {
                // Connect to the target machine on a new socket..
                this.m_vServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                this.m_vServerSocket.BeginConnect(new IPEndPoint(ipAddress, remotePort),
                    new AsyncCallback((result) =>
                    {
                        // Ensure the connection was valid..
                        if (result == null || result.IsCompleted == false || !(result.AsyncState is Socket))
                            return;

                        // Obtain our server instance. (YOU NEED TO USE IT LIKE THIS DO NOT USE this.m_vServerSocket here!)
                        Socket serverSocket = (result.AsyncState as Socket);

                        // Stop processing if the server has told us to stop..
                        if (this.m_vIsRunning == false || serverSocket == null)
                            return;

                        // Complete the async connection request..
                        serverSocket.EndConnect(result);

                        // Start monitoring for packets..
                        this.m_vClientSocket.ReceiveBufferSize = MAX_BUFFER_SIZE;
                        serverSocket.ReceiveBufferSize = MAX_BUFFER_SIZE;
                        this.Server_BeginReceive();
                        this.Client_BeginReceive();
                    }), this.m_vServerSocket);

                return true;
            }
            catch (ObjectDisposedException ex)
            {
                // Process the exception as you wish here..
            }
            catch (SocketException ex)
            {
                // Process the exception as you wish here..
            }
            catch (Exception ex)
            {
                // Process the exception as you wish here..
            }

            return false;
        }

        /// <summary>
        /// Stops this client object.
        /// </summary>
        public void Stop()
        {
            if (this.m_vIsRunning == false)
                return;

            // Cleanup the client socket..
            if (this.m_vClientSocket != null)
                this.m_vClientSocket.Close();
            this.m_vClientSocket = null;

            // Cleanup the server socket..
            if (this.m_vServerSocket != null)
                this.m_vServerSocket.Close();
            this.m_vServerSocket = null;

            this.m_vIsRunning = false;
        }

        /// <summary>
        /// Begins an async event to receive incoming data.
        /// </summary>
        private void Client_BeginReceive()
        {
            // Prevent invalid call..
            if (!this.m_vIsRunning)
                return;

            try
            {
                this.m_vClientSocket.BeginReceive(this.m_vClientBuffer, 0, MAX_BUFFER_SIZE, SocketFlags.None, new AsyncCallback(OnClientReceiveData), this.m_vClientSocket);
            }
            catch (SocketException ex) { this.Stop(); }
            catch (Exception ex) { this.Stop(); }
        }

        /// <summary>
        /// Begins an async event to receive incoming data. 
        /// </summary>
        private void Server_BeginReceive()
        {
            // Prevent invalid call..
            if (!this.m_vIsRunning)
                return;

            try
            {
                this.m_vServerSocket.BeginReceive(this.m_vServerBuffer, 0, MAX_BUFFER_SIZE, SocketFlags.None, new AsyncCallback(OnServerReceiveData), this.m_vServerSocket);
            }
            catch (SocketException ex) { this.Stop(); }
            catch (Exception ex) { this.Stop(); }
        }

        /// <summary>
        /// Completes an async event to receive data.
        /// </summary>
        /// <param name="result"></param>
        private void OnClientReceiveData(IAsyncResult result)
        {
            // Prevent invalid calls to this function..
            if (!this.m_vIsRunning || result.IsCompleted == false || !(result.AsyncState is Socket))
            {
                this.Stop();
                return;
            }

            Socket client = (result.AsyncState as Socket);

            // Attempt to end the async call..
            Int32 nRecvCount = 0;
            try
            {
                nRecvCount = client.EndReceive(result);
                if (nRecvCount == 0)
                {
                    this.Stop();
                    return;
                }
            }
            catch { this.Stop(); return; }

            // Read the current packet..
            Byte[] btRecvData = new Byte[nRecvCount];
            Array.Copy(this.m_vClientBuffer, 0, btRecvData, 0, nRecvCount);

            // Send the packet to the server..
            this.SendToServer(btRecvData);
            Log("server", btRecvData);

            // Begin listening for next packet..
            this.Client_BeginReceive();
        }

        /// <summary>
        /// Completes an async event to receive data.
        /// </summary>
        /// <param name="result"></param>
        private void OnServerReceiveData(IAsyncResult result)
        {
            // Prevent invalid calls to this function..
            if (!this.m_vIsRunning || result.IsCompleted == false || !(result.AsyncState is Socket))
            {
                this.Stop();
                return;
            }

            Socket server = (result.AsyncState as Socket);

            // Attempt to end the async call..
            Int32 nRecvCount = 0;
            try
            {
                nRecvCount = server.EndReceive(result);
                if (nRecvCount == 0)
                {
                    this.Stop();
                    return;
                }
            }
            catch { this.Stop(); return; }

            // Read the current packet..
            Byte[] btRecvData = new Byte[nRecvCount];
            Array.Copy(this.m_vServerBuffer, 0, btRecvData, 0, nRecvCount);

            // Send the packet to the client..
            this.SendToClient(btRecvData);
            this.Log("client", btRecvData);

            // Begin listening for next packet..
            this.Server_BeginReceive();
        }

        public byte[] Decrypt(byte[] bytes)
        {
            byte[] newBytes = bytes;

            //Decrypt the bytes using Caesar's cypher.
            for (int i = 0; i < bytes.Length; i++)
            {

                //Console.WriteLine(newBytes[i]);
                newBytes[i]--;
            }

            return newBytes;
        }

        public void Log(string mode, byte[] bytes)
        {

            //Convert the byte array to a string.
            string packet = System.Text.Encoding.ASCII.GetString(Decrypt(bytes));

            if (mode == "client")
            {
                //Configure the console for correct coloration.
                Console.ForegroundColor = ConsoleColor.Yellow;

                //Print the packet.
                Console.WriteLine(packet);
                Console.WriteLine();

                //Reset the console for other coloring.
                Console.ResetColor();
            }

            if (mode == "server")
            {
                //Configure the console for correct coloration.
                Console.ForegroundColor = ConsoleColor.Green;

                //Print the packet.
                Console.WriteLine(packet);
                Console.WriteLine();

                //Reset the console for other coloring.
                Console.ResetColor();

            }
        }

        /// <summary>
        /// Sends the given packet data to the client socket.
        /// </summary>
        /// <param name="btPacket"></param>
        public void SendToClient(byte[] btPacket)
        {
            if (!this.m_vIsRunning)
                return;

            try
            {
                this.m_vClientSocket.BeginSend(btPacket, 0, btPacket.Length, SocketFlags.None,
                    new AsyncCallback((x) =>
                    {
                        if (x.IsCompleted == false || !(x.AsyncState is Socket))
                        {
                            this.Stop();
                            return;
                        }

                        (x.AsyncState as Socket).EndSend(x);
                    }), this.m_vClientSocket);
            }
            catch (Exception ex) { this.Stop(); }
        }

        /// <summary>
        /// Sends the given packet data to the server socket.
        /// </summary>
        /// <param name="btPacket"></param>
        public void SendToServer(byte[] btPacket)
        {        
            if (!this.m_vIsRunning)
                return;

            try
            {
                this.m_vServerSocket.BeginSend(btPacket, 0, btPacket.Length, SocketFlags.None,
                    new AsyncCallback((x) =>
                    {
                        if (x.IsCompleted == false || !(x.AsyncState is Socket))
                        {
                            this.Stop();
                            return;
                        }

                        (x.AsyncState as Socket).EndSend(x);
                    }), this.m_vServerSocket);
            }
            catch (Exception ex) { this.Stop(); }
        }

        /// <summary>
        /// Gets the base client socket.
        /// </summary>
        public Socket ClientSocket
        {
            get
            {
                if (this.m_vIsRunning && this.m_vClientSocket != null)
                    return this.m_vClientSocket;
                return null;
            }
        }

        /// <summary>
        /// Gets the base server socket.
        /// </summary>
        public Socket ServerSocket
        {
            get
            {
                if (this.m_vIsRunning && this.m_vServerSocket != null)
                    return this.m_vServerSocket;
                return null;
            }
        }
    }
}
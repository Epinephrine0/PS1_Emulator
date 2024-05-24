using System;
using System.Net;
using System.Net.Sockets;

namespace PSXEmulator.Peripherals.IO {
    public class Server : Socket {
        IPAddress ipAddress = IPAddress.Parse("127.0.0.1"); // Localhost
        int port = 1234;
        TcpClient client;
        NetworkStream Stream;
        TcpListener listener;
        AsyncCallback DataReceivedHandler;
        public byte[] buffer = new byte[2];
        public Server(AsyncCallback handler) { 
            DataReceivedHandler = handler;
        }

        public void AcceptClientConnection() {
            listener = new TcpListener(ipAddress, port);
            Console.WriteLine("[SIO1] Server started, waiting for a connection...");
            listener.Start();
            listener.BeginAcceptTcpClient(new AsyncCallback(ClientConnected), null);
        }
        
        public byte[] Receive() {
            return buffer;
        }

        public void Send(byte[] buffer) {
            if (buffer.Length > 2 || Stream == null) {
                throw new Exception();
            }
            Stream.Write(buffer, 0, buffer.Length);
        }
        public void BeginReceiving() {
            if (DataReceivedHandler == null) {
                throw new NullReferenceException("DataReceivedHandler == null");
            }
            Stream.BeginRead(buffer, 0, 2, DataReceivedHandler, null);
        }
        public void Stop(IAsyncResult result) {
            Stream.EndRead(result);
        }

        public void ClientConnected(IAsyncResult result) {
            if (!listener.Server.IsBound) { return; }   

            try {
                client = listener.EndAcceptTcpClient(result);
                Stream = client.GetStream();
                IPEndPoint? remoteIpEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                Console.WriteLine("Client Connected: " + remoteIpEndPoint?.Address + ":" + remoteIpEndPoint?.Port);
                BeginReceiving();
            } catch (Exception e) {
                Console.WriteLine("[SIO1] Error: " + e.Message);
            }
        }

        public void ConnectToServer() {
            throw new NotSupportedException();
        }

        public bool IsConnected() {
            return Stream != null;
        }
        public void Terminate() {
            if (Stream != null) {
                Stream.Close();
            }

            if (client != null) {
                client.Close();
            }

            if (listener != null) {
                listener.Stop();
            }
        }
    }
}

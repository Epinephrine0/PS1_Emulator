using System;

namespace PSXSharp.Peripherals.IO {
    public interface Socket {
        public void Send(byte[] buffer);
        public byte[] Receive();

        public void ConnectToServer();
        public void AcceptClientConnection();

        public void BeginReceiving();
        public void Stop(IAsyncResult result);

        public bool IsConnected();

        public void Terminate();

    }
}

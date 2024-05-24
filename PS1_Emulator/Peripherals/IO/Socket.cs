namespace PSXEmulator.Peripherals.IO {
    public interface Socket {
        public void Send(byte[] buffer);
        public byte[] Receive();

        public void ConnectToServer();
        public void AcceptClientConnection();

    }
}

namespace NetSdrClientApp.Networking
{
    public interface ITcpClient
    {
        void Connect();
        void Disconnect();
        Task SendMessageAsync(byte[] data);

        event EventHandler<byte[]> MessageReceived;
        public bool Connected { get; }
    }
}

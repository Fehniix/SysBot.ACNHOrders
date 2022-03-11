using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Base;
using static SysBot.Base.SwitchOffsetType;

namespace SocketAPI
{
	public class DedicatedConnection: SysBot.Base.SwitchSocket
	{
		/// <summary>
		/// The console's `TcpClient`.
		/// </summary>
		private TcpClient? consoleConnection;

		/// <summary>
		/// The `TcpClient` stream.
		/// </summary>
		private NetworkStream? consoleConnectionStream;

		/// <summary>
		/// Connection config object provided at initialization time.
		/// </summary>
		private IWirelessConnectionConfig config;
		
		public DedicatedConnection(IWirelessConnectionConfig config): base(config) 
		{
			this.config = config;
		}

		/// <summary>
		/// Connects to the console using configs provided at initialization time.
		/// </summary>
		public async Task Start()
		{
			await this.Start(this.config);
		}

		/// <summary>
		/// Connects to the console.
		/// </summary>
		public async Task Start(IWirelessConnectionConfig config)
		{
		 	await this.Start(config.IP, config.Port);
		}

		/// <summary>
		/// Connects to the console.
		/// </summary>
		public async Task Start(string ip, int port)
		{
			this.consoleConnection = new();
            try
            {
                await this.consoleConnection.ConnectAsync(ip, port);
            }
            catch(Exception ex)
            {
                Logger.LogError($"Dedicated connection could not be established. Error: {ex.Message}");
                return;
            }

			Logger.LogInfo("Dedicated connection with console opened.");
			
			this.consoleConnectionStream = this.consoleConnection.GetStream();

            string version = await this.GetVersion(new());
            Logger.LogInfo($"Requested sysbot-base version as first interaction: {version}");
		}

		/// <summary>
		/// Loads development configuration from local .env file (case-insensitive).
		/// </summary>
		public static SocketAPIConsoleConnectionConfig? LoadDevConfigs()
		{
			Dictionary<string, object>? envEntries = EnvParser.ParseFile(".env");

			if (envEntries == null)
				return null;
			
			if (envEntries.Count < 2)
				return null;

			if (envEntries["ip"] == null || envEntries["port"] == null)
				return null;

			SocketAPIConsoleConnectionConfig devConfigs = new();
			devConfigs.IP = (string)envEntries["ip"];
			devConfigs.Port = System.Int32.Parse((string)envEntries["port"]);

			if (!IPAddress.TryParse(devConfigs.IP, out _))
				devConfigs.IP = Dns.GetHostEntry(devConfigs.IP).AddressList[0]?.MapToIPv4().ToString() ?? IPAddress.Loopback.MapToIPv4().ToString();

			return devConfigs;
		}

		public override void Connect() {}
		public override void Disconnect() {}
		public override void Reset() {}

        private int Read(byte[] buffer)
        {
			int currentIndex = 0;
			int _byte = this.consoleConnectionStream!.ReadByte();
			while(_byte != -1 && (byte)_byte != (byte)'\n')
			{
				buffer[currentIndex++] = (byte)_byte;
				_byte = this.consoleConnectionStream!.ReadByte();
			}
	
			return currentIndex;
        }

        public async Task SendAsync(byte[] buffer, CancellationToken token)
		{
			await this.consoleConnectionStream!.WriteAsync(buffer, 0, buffer.Length);
		}

        private async Task<byte[]> ReadBytesFromCmdAsync(byte[] cmd, int length, CancellationToken token)
        {
            await SendAsync(cmd, token).ConfigureAwait(false);
            var buffer = new byte[(length * 2) + 1];
            _ = Read(buffer);
            return SysBot.Base.Decoder.ConvertHexByteStringToBytes(buffer);
        }

        public async Task<byte[]> ReadBytesAsync(uint offset, int length, CancellationToken token) => await Read(offset, length, Heap, token).ConfigureAwait(false);
        public async Task<byte[]> ReadBytesMainAsync(ulong offset, int length, CancellationToken token) => await Read(offset, length, Main, token).ConfigureAwait(false);
        public async Task<byte[]> ReadBytesAbsoluteAsync(ulong offset, int length, CancellationToken token) => await Read(offset, length, Absolute, token).ConfigureAwait(false);

        public async Task WriteBytesAsync(byte[] data, uint offset, CancellationToken token) => await Write(data, offset, Heap, token).ConfigureAwait(false);
        public async Task WriteBytesMainAsync(byte[] data, ulong offset, CancellationToken token) => await Write(data, offset, Main, token).ConfigureAwait(false);
        public async Task WriteBytesAbsoluteAsync(byte[] data, ulong offset, CancellationToken token) => await Write(data, offset, Absolute, token).ConfigureAwait(false);

        public async Task<ulong> GetMainNsoBaseAsync(CancellationToken token)
        {
            byte[] baseBytes = await ReadBytesFromCmdAsync(SwitchCommand.GetMainNsoBase(), sizeof(ulong), token).ConfigureAwait(false);
            Array.Reverse(baseBytes, 0, 8);
            return BitConverter.ToUInt64(baseBytes, 0);
        }

        public async Task<ulong> GetHeapBaseAsync(CancellationToken token)
        {
            var baseBytes = await ReadBytesFromCmdAsync(SwitchCommand.GetHeapBase(), sizeof(ulong), token).ConfigureAwait(false);
            Array.Reverse(baseBytes, 0, 8);
            return BitConverter.ToUInt64(baseBytes, 0);
        }

        private async Task<byte[]> Read(ulong offset, int length, SwitchOffsetType type, CancellationToken token)
        {
            var method = type.GetReadMethod();
            if (length <= MaximumTransferSize)
            {
                var cmd = method(offset, length);
                return await ReadBytesFromCmdAsync(cmd, length, token).ConfigureAwait(false);
            }

            byte[] result = new byte[length];
            for (int i = 0; i < length; i += MaximumTransferSize)
            {
                int len = MaximumTransferSize;
                int delta = length - i;
                if (delta < MaximumTransferSize)
                    len = delta;

                var cmd = method(offset + (uint)i, len);
                var bytes = await ReadBytesFromCmdAsync(cmd, len, token).ConfigureAwait(false);
                bytes.CopyTo(result, i);
                await Task.Delay((MaximumTransferSize / DelayFactor) + BaseDelay, token).ConfigureAwait(false);
            }
            return result;
        }

        private async Task Write(byte[] data, ulong offset, SwitchOffsetType type, CancellationToken token)
        {
            var method = type.GetWriteMethod();
            if (data.Length <= MaximumTransferSize)
            {
                var cmd = method(offset, data);
                await SendAsync(cmd, token).ConfigureAwait(false);
                return;
            }
            int byteCount = data.Length;
            for (int i = 0; i < byteCount; i += MaximumTransferSize)
            {
                var slice = data.SliceSafe(i, MaximumTransferSize);
                var cmd = method(offset + (uint)i, slice);
                await SendAsync(cmd, token).ConfigureAwait(false);
                await Task.Delay((MaximumTransferSize / DelayFactor) + BaseDelay, token).ConfigureAwait(false);
            }
        }

        public async Task<byte[]> ReadRaw(byte[] command, int length, CancellationToken token)
        {
            await SendAsync(command, token).ConfigureAwait(false);
            var buffer = new byte[length];
            var _ = Read(buffer);
            return buffer;
        }

        public async Task SendRaw(byte[] command, CancellationToken token)
        {
            await SendAsync(command, token).ConfigureAwait(false);
        }

		public async Task<string> GetVersion(CancellationToken token)
		{
			string command = "getVersion\r\n";
			byte[] commandBytes = System.Text.Encoding.ASCII.GetBytes(command);

			byte[] result = await this.ReadRaw(commandBytes, 16, token);
			return System.Text.Encoding.ASCII.GetString(result);
		}
	}

	public static class SwitchOffsetTypeExtensions
    {
        /// <summary>
        /// Gets the Peek command encoder for the input <see cref="SwitchOffsetType"/>
        /// </summary>
        /// <param name="type">Offset type</param>
        /// <param name="crlf">Protocol uses CRLF to terminate messages?</param>
        public static Func<ulong, int, byte[]> GetReadMethod(this SwitchOffsetType type, bool crlf = true) => type switch
        {
            SwitchOffsetType.Heap => (o, c) => SwitchCommand.Peek((uint)o, c, crlf),
            SwitchOffsetType.Main => (o, c) => SwitchCommand.PeekMain(o, c, crlf),
            SwitchOffsetType.Absolute => (o, c) => SwitchCommand.PeekAbsolute(o, c, crlf),
            _ => throw new IndexOutOfRangeException("Invalid offset type."),
        };

        /// <summary>
        /// Gets the Poke command encoder for the input <see cref="SwitchOffsetType"/>
        /// </summary>
        /// <param name="type">Offset type</param>
        /// <param name="crlf">Protocol uses CRLF to terminate messages?</param>
        public static Func<ulong, byte[], byte[]> GetWriteMethod(this SwitchOffsetType type, bool crlf = true) => type switch
        {
            SwitchOffsetType.Heap => (o, b) => SwitchCommand.Poke((uint)o, b, crlf),
            SwitchOffsetType.Main => (o, b) => SwitchCommand.PokeMain(o, b, crlf),
            SwitchOffsetType.Absolute => (o, b) => SwitchCommand.PokeAbsolute(o, b, crlf),
            _ => throw new IndexOutOfRangeException("Invalid offset type."),
        };
    }

	internal static class ArrayUtil
    {
        public static byte[] SliceSafe(this byte[] src, int offset, int length)
        {
            var delta = src.Length - offset;
            if (delta < length)
                length = delta;

            byte[] data = new byte[length];
            Buffer.BlockCopy(src, offset, data, 0, data.Length);
            return data;
        }
    }
}
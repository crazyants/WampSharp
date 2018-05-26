﻿using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using WampSharp.Core.Listener;
using WampSharp.Core.Message;
using WampSharp.RawSocket;
using WampSharp.V2.Binding.Parsers;

namespace WampSharp.AspNetCore.RawSocket
{
    public class RawSocketConnection<TMessage> : AsyncWampConnection<TMessage>
    {
        private readonly ConnectionContext mConnection;

        private const int FrameHeaderSize = 4;

        private readonly RawSocketFrameHeaderParser mFrameHeaderParser = new RawSocketFrameHeaderParser();

        private readonly int mMaxAllowedMessageSize;

        private IWampStreamingMessageParser<TMessage> mParser;

        private bool mIsConnected = true;

        public RawSocketConnection(SocketData connection, IWampStreamingMessageParser<TMessage> parser)
        {
            mParser = parser;
            mConnection = connection.ConnectionContext;
            mMaxAllowedMessageSize = connection.Handshake.MaxMessageSizeInBytes;
        }

        protected override async Task SendAsync(WampMessage<object> message)
        {
            ReadOnlyMemory<byte> bytes = GetBytes(message);
            
            await Writer.WriteAsync(bytes).ConfigureAwait(false);
        }

        public async Task RunAsync()
        {
            try
            {
                while (mIsConnected)
                {
                    ReadResult result = await Reader.ReadAsync()
                                                    .ConfigureAwait(false);

                    ReadOnlySequence<byte> buffer = result.Buffer;

                    try
                    {
                        if (result.IsCanceled || result.IsCompleted)
                        {
                            mIsConnected = false;
                        }

                        if (!buffer.IsEmpty)
                        {
                            ProcessBuffer(buffer);
                        }
                    }
                    finally
                    {
                        Reader.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseConnectionError(ex);
                mIsConnected = false;
            }

            RaiseConnectionClosed();
        }

        private void ProcessBuffer(in ReadOnlySequence<byte> buffer)
        {
            ReadOnlySequence<byte> headerBytes =
                buffer.Slice(0, FrameHeaderSize);

            FrameType frameType;
            int messageLength;

            if (mFrameHeaderParser.TryParse(headerBytes, out frameType, out messageLength) &&
                (messageLength <= mMaxAllowedMessageSize))
            {
                ReadOnlySequence<byte> frameContent =
                    buffer.Slice(FrameHeaderSize, messageLength);

                HandleFrame(frameType, frameContent);
            }
            else
            {
                this.Dispose();
            }
        }

        private void HandleFrame(FrameType frameType, in ReadOnlySequence<byte> message)
        {
            switch (frameType)
            {
                case FrameType.WampMessage:
                    HandleWampFrame(message);
                    break;
            }
        }

        private void HandleWampFrame(in ReadOnlySequence<byte> message)
        {
            WampMessage<TMessage> parsed = ParseMessage(message);
            RaiseMessageArrived(parsed);
        }

        // TODO: improve this.
        private WampMessage<TMessage> ParseMessage(ReadOnlySequence<byte> messageInBytes)
        {
            ArraySegment<byte> segment = messageInBytes.ToArraySegment();
            MemoryStream memoryStream = new MemoryStream(segment.Array, segment.Offset, segment.Count);
            return mParser.Parse(memoryStream);
        }

        // TODO: improve this.
        private ReadOnlyMemory<byte> GetBytes(WampMessage<object> message)
        {
            int headerSize = FrameHeaderSize;

            MemoryStream memoryStream = new MemoryStream(headerSize);
            memoryStream.Position = headerSize;
            mParser.Format(message, memoryStream);

            byte[] buffer = memoryStream.GetBuffer();

            mFrameHeaderParser.WriteHeader(FrameType.WampMessage,
                (int)memoryStream.Length - headerSize,
                buffer);

            return new ReadOnlyMemory<byte>(memoryStream.ToArray());
        }

        protected override void Dispose()
        {
            Reader.CancelPendingRead();
            Reader.Complete();
            Writer.Complete();
        }

        protected override bool IsConnected
        {
            get
            {
                return mIsConnected;
            }
        }

        private PipeReader Reader
        {
            get
            {
                return mConnection.Transport.Input;
            }
        }

        private PipeWriter Writer
        {
            get
            {
                return mConnection.Transport.Output;
            }
        }
    }
}
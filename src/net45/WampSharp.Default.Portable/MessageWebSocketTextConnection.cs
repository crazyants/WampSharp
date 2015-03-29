﻿using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using WampSharp.Core.Message;
using WampSharp.V2.Binding;

namespace WampSharp.Default
{
    public class MessageWebSocketTextConnection<TMessage> : MessageWebSocketConnection<TMessage>
    {
        private readonly MessageWebSocket mWebSocket;
        private readonly IWampTextBinding<TMessage> mTextBinding;

        public MessageWebSocketTextConnection(MessageWebSocket webSocket, string uri, IWampBinding<TMessage> binding) : 
            base(webSocket, uri, binding, SocketMessageType.Utf8)
        {
            mWebSocket = webSocket;
        }

        public MessageWebSocketTextConnection(string uri, IWampTextBinding<TMessage> binding) : 
            this(new MessageWebSocket(), uri, binding)
        {
            mTextBinding = binding;
        }

        protected override void OnMessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            try
            {
                Stream stream = args.GetDataStream().AsStreamForRead();

                StreamReader reader = new StreamReader(stream);

                string frame = reader.ReadToEnd();

                WampMessage<TMessage> message = mTextBinding.Parse(frame);

                RaiseMessageArrived(message);
            }
            catch (Exception ex)
            {
                RaiseConnectionError(ex);
            }
        }

        protected override async Task SendAsync(WampMessage<object> message)
        {
            try
            {
                Stream stream = mWebSocket.OutputStream.AsStreamForWrite();

                string frame = mTextBinding.Format(message);

                StreamWriter streamWriter = new StreamWriter(stream);

                await streamWriter.WriteAsync(frame);

                await streamWriter.FlushAsync();
            }
            catch (Exception ex)
            {
                RaiseConnectionError(ex);
                throw;
            }
        }
    }
}
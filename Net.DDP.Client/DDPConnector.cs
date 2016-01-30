﻿using System;
using WebSocket4Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Threading;
using System.Reactive.Subjects;
using SuperSocket.ClientEngine;

namespace Net.DDP.Client
{
    internal class DDPConnector
    {
        private const string ConnectDDPMessage = "{\"msg\":\"connect\",\"version\":\"pre1\",\"support\":[\"pre1\"]}";

        private WebSocket _socket;
        private IObservable<DDPMessage> _messageStream;
        private IObservable<DDPMessage> _errorStream;
        private IObservable<DDPMessage> _closedStream;
        private IConnectableObservable<DDPMessage> _mergedStream;

        private string _url = string.Empty;

        public IObservable<DDPMessage> Connect (string url)
        {
            if (string.IsNullOrWhiteSpace (url)) {
                throw new ArgumentNullException ("url");
            }

            _url = url;
            _socket = new WebSocket (_url);

            _messageStream = Observable.FromEventPattern<MessageReceivedEventArgs> (
                handler => _socket.MessageReceived += handler, 
                handler => _socket.MessageReceived -= handler)
				.Do (m => Debug.WriteLine ("T {0} - DDPClient - Incoming: {1}", Thread.CurrentThread.ManagedThreadId, 
                m.EventArgs.Message))
                .Select (rawMessage => DeserializeOrReturnErrorObject (rawMessage.EventArgs.Message));

            _errorStream = Observable.FromEventPattern<ErrorEventArgs> (
                handler => _socket.Error += handler, 
                handler => _socket.Error -= handler)
                .Do (m => Debug.WriteLine ("T {0} - DDPClient - Error: {1}", Thread.CurrentThread.ManagedThreadId, 
                m.EventArgs.Exception))
                .Select (m => BuildErrorDDPMessage ());

            _closedStream = Observable.FromEventPattern (
                handler => _socket.Closed += handler, 
                handler => _socket.Closed -= handler)
                .Do (m => Debug.WriteLine ("T {0} - DDPClient - Socket closed", Thread.CurrentThread.ManagedThreadId))
                .SelectMany (m => Observable.Throw <DDPMessage> (new WebsocketConnectionException ("Websocket was closed")));

            _mergedStream = Observable.Merge (new IObservable<DDPMessage> [] {
                _messageStream, _errorStream, _closedStream
            }).Publish ();

            _socket.Opened += _socket_Opened;

            _mergedStream.Connect ();

            _socket.Open ();

            return _mergedStream;
        }

        public void Close ()
        {
            _socket.Close ();
        }

        public void Send (string message)
        {
            Debug.WriteLine ("T {0} - DDPClient - Outgoing: {1} ", System.Threading.Thread.CurrentThread.ManagedThreadId, message);
            _socket.Send (message);
        }

        void _socket_Opened (object sender, EventArgs e)
        {
            this.Send (ConnectDDPMessage);
        }

        private DDPMessage DeserializeOrReturnErrorObject (string jsonString)
        {
            try {
                return JsonConvert.DeserializeObject<DDPMessage> (jsonString);
            } catch (Exception e) {
                Debug.WriteLine (e);
                // Sacrificing thoroughness for the sake of simplicity here. Good tradeoff in this specific case.
                return BuildErrorDDPMessage ();
            }
        }

        private DDPMessage BuildErrorDDPMessage ()
        {
            var ddpMessage = new DDPMessage ();
            ddpMessage.Type = DDPType.Error;
            ddpMessage.Error = new Error (null, null, null, DDPErrorType.ClientError);
            return ddpMessage;
        }
    }
}

﻿using Newtonsoft.Json;
using System.Diagnostics;
using System.Reactive.Linq;
using System;
using System.Threading;

namespace Net.DDP.Client
{
    public class DDPClient
    {
        public const string DDP_PROPS_MESSAGE = "msg";
        public const string DDP_PROPS_ID = "id";
        public const string DDP_PROPS_COLLECTION = "collection";
        public const string DDP_PROPS_FIELDS = "fields";
        public const string DDP_PROPS_SESSION = "session";
        public const string DDP_PROPS_RESULT = "result";
        public const string DDP_PROPS_ERROR = "error";
        public const string DDP_PROPS_SUBS = "subs";
        public const string DDP_PROPS_METHODS = "methods";
        public const string DDP_PROPS_VERSION = "version";

        private static Random random = new Random ();
        private static object randomLock = new Object ();

        private readonly DDPConnector _connector;

        private IObservable<DDPMessage> rawStream;

        public DDPClient ()
        {
            _connector = new DDPConnector ();
        }

        public IObservable<DDPMessage> Connect (string url)
        {
            ValidateUrl (url);
            rawStream = _connector.Connect (url);
            return rawStream.Where (message => DDPType.Connected.Equals (message?.Type)
            || DDPType.Failed.Equals (message.Type))
                    .Do (m => Debug.WriteLine ("Connect response: " + m));
        }

        public IObservable<DDPMessage> Call (string methodName, params object[] args)
        {
            var id = NextId ();
            string message = string.Format ("\"msg\": \"method\",\"method\": \"{0}\",\"params\": {1},\"id\": \"{2}\"",
                                 methodName, CreateJSonArray (args), id);
            message = "{" + message + "}";
            _connector.Send (message);
            return rawStream.Where (ddpMessage => IsRelatedToMethodCall (id, ddpMessage))
                .Do (m => Debug.WriteLine ("Method call response: " + m));
        }

        public IObservable<DDPMessage> Subscribe (string subscribeTo, params object[] args)
        {
            var id = NextId ();
            string message = string.Format ("\"msg\": \"sub\",\"name\": \"{0}\",\"params\": [{1}],\"id\": \"{2}\"",
                                 subscribeTo, CreateJSonArray (args), id);
            message = "{" + message + "}";
            _connector.Send (message);
            return rawStream.Where (ddpMessage => IsRelatedToSubscription (id, ddpMessage, subscribeTo))
                .Do (m => Debug.WriteLine ("Subscription call response: " + m));
        }

        public IObservable<DDPMessage> GetCollectionStream (string collectionName)
        {
            return rawStream.Where (ddpMessage => IsRelatedToCollection (collectionName, ddpMessage));
        }

        private string CreateJSonArray (params object[] args)
        {
            if (args == null)
                return "[]";
            
            return JsonConvert.SerializeObject (args);
        }

        private int NextId ()
        {
            lock (randomLock) {
                return random.Next ();
            }
        }

        private bool IsRelatedToMethodCall (int id, DDPMessage message)
        {
            return id.ToString ().Equals (message?.Id)
            || (DDPType.Updated.Equals (message?.Type)
            && message?.Methods != null && message.Methods.Contains (id.ToString ())); 
        }

        private bool IsRelatedToSubscription (int id, DDPMessage message, string collection)
        {
            return id.ToString ().Equals (message?.Id)
            || (DDPType.Ready.Equals (message?.Type)
            && message?.Subs != null && message.Subs.Contains (id.ToString ()))
            || (collection != null && collection.Equals (message?.Collection));
        }

        private bool IsRelatedToCollection (string collection, DDPMessage message)
        {
            return collection != null && collection.Equals (message?.Collection);
        }

        private void  ValidateUrl (string url)
        {
            // TODO Strengthen this validation. The default Uri.IsWellFormedUriString won't handle ws/wss schemas.
            if (string.IsNullOrWhiteSpace (url)
                || (!url.StartsWith ("ws://") && !url.StartsWith ("wss://"))) {
                throw new ArgumentException ("Invalid url: " + url, "url");
            }
        }
    }
}

﻿using System.Collections.Generic;
using System.Globalization;
using System.Dynamic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System;

namespace Net.DDP.Client
{
	internal class JsonDeserializeHelper
	{
		private readonly IDataSubscriber _subscriber;

		public JsonDeserializeHelper(IDataSubscriber subscriber)
		{
			_subscriber = subscriber;
		}

		internal void Deserialize(string jsonItem)
		{
			Debug.WriteLine ("Raw message: " + jsonItem);
			JObject jObj = JObject.Parse(jsonItem);

			if (jObj[DDPClient.DDP_PROPS_MESSAGE] != null && jObj[DDPClient.DDP_PROPS_MESSAGE].ToString() == "error")
				HandleError(jObj[DDPClient.DDP_PROPS_ERROR] ?? jObj);
			else
				HandleSubResult(jObj);
		}

		private void HandleError(JToken jError)
		{
			if (jError == null)
				return;

			dynamic entity = new ExpandoObject();
			entity.Type = DDPType.Error;

			if (jError["error"] != null)
				entity.Code = jError["error"].ToString();
			if (jError["reason"] != null)
				entity.Error = jError["reason"].ToString();
			if (jError["details"] != null)
				entity.Details = jError["details"].ToString();
			if (jError["offendingMessage"] != null)
				entity.OffendingMessage = jError["offendingMessage"].ToString();
			//TODO Possibly delete this next case, as it appears unused on the latest DDP version spec
			if (jError["message"] != null)
				entity.Message = jError["message"].ToString();

			_subscriber.DataReceived(entity);
		}

		private void HandleSubResult(JObject jObj)
		{
			dynamic entity = new ExpandoObject();

			if (jObj [DDPClient.DDP_PROPS_MESSAGE] == null) {
				// The server can actually return messages without the message property. Those should be ignored.
				return;
			}

			var message = jObj [DDPClient.DDP_PROPS_MESSAGE].ToString ();

			if (message == DDPClient.DDP_MESSAGE_TYPE_ADDED) {
				entity = GetMessageData (jObj);
				entity.Type = DDPType.Added;
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_CHANGED) {
				entity = GetMessageData (jObj);
				entity.Type = DDPType.Changed;
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_NOSUB) {
				HandleError (jObj [DDPClient.DDP_PROPS_ERROR]);
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_READY) {
				entity.RequestsIds = ((JArray)jObj [DDPClient.DDP_PROPS_SUBS]).Select (id => id.Value<int> ()).ToArray ();
				entity.Type = DDPType.Ready;
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_REMOVED) {
				entity = GetMessageData (jObj);
				entity.Type = DDPType.Removed;
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_RESULT) {
				entity = GetMessageDataRecursive (jObj);
				entity.Type = DDPType.MethodResult;
				entity.RequestingId = jObj [DDPClient.DDP_PROPS_ID].ToString ();
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_UPDATED) {
				entity.Methods = jObj [DDPClient.DDP_PROPS_METHODS].Select (id => id.Value<int> ()).ToList ();
				entity.Type = DDPType.Updated;
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_CONNECTED) {
				entity.Session = jObj [DDPClient.DDP_PROPS_SESSION].ToString();
				entity.Type = DDPType.Connected;
			} else if (message == DDPClient.DDP_MESSAGE_TYPE_FAILED) {
				entity.Version = jObj [DDPClient.DDP_PROPS_VERSION].ToString();
				entity.Type = DDPType.Failed;
			}

			_subscriber.DataReceived(entity);
		}

		private dynamic GetMessageData(JObject json)
		{
			var tmp = (JObject)json[DDPClient.DDP_PROPS_FIELDS];
			dynamic entity = GetMessageDataRecursive(tmp);
			entity.Id = json[DDPClient.DDP_PROPS_ID].ToString();
			entity.Collection = json[DDPClient.DDP_PROPS_COLLECTION].ToString();

			return entity;
		}

		private string UppercaseFirst(string s)
		{
			// Check for empty string.
			if (string.IsNullOrEmpty(s))
			{
				return string.Empty;
			}
			// Return char and concat substring.
			return char.ToUpper(s[0]) + s.Substring(1);
		}

		private dynamic GetMessageDataRecursive(JObject json)
		{
			dynamic entity = new ExpandoObject();
			var entityAsCollection = (IDictionary<string, object>) entity;

			if (json == null) return entityAsCollection;

			foreach (var item in json)
			{
				string propertyName = UppercaseFirst (item.Key);
				if (item.Value is JObject) // Property is an object
					entityAsCollection.Add(propertyName, (IDictionary<string, object>)GetMessageDataRecursive((JObject) item.Value));
				else if (item.Value is JArray) // Property is an array...
				{
					JArray collection = (JArray) item.Value;
					if (collection.Count == 0)
						continue;
					List<dynamic> entityCollection = new List<dynamic> ();
					foreach (object element in collection) {
						if (element is JObject) {
							entityCollection.Add (GetMessageDataRecursive ((JObject) element));
						} else {
							entityCollection.Add (element.ToString ());
						}
					}
					entityAsCollection.Add(propertyName, entityCollection);
				}
				else // Property is a string
					entityAsCollection.Add(propertyName, item.Value.ToString());
			}

			return entityAsCollection;
		}
}

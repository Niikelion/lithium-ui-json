using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UI.Li.Json
{
    [Serializable]
    public class SerializableJson: ISerializationCallbackReceiver
    {
        public JToken Value = JValue.CreateNull();

        [SerializeField] private string data = "null";

        public void OnBeforeSerialize()
        {
            string nData = Value.ToString(Formatting.None);
            if (data != null && nData != data)
                data = nData;
        }

        public void OnAfterDeserialize() => Value = JToken.Parse(data);
    }
}
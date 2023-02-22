using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.Serialization;

namespace UI.Li.Json
{
    [Serializable]
    public class SerializableJson: ISerializable
    {
        public JToken Value;

        private string data = "null";

        public SerializableJson()
        {
            Value = JToken.Parse(data);
        }
        
        public SerializableJson(SerializationInfo info, StreamingContext context)
        {
            data = (string) info.GetValue("data", typeof(string));
            Value = JToken.Parse(data);
        }
        
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            data = Value.ToString(Formatting.None);
            info.AddValue("data", data, typeof(string));
        }
    }
}
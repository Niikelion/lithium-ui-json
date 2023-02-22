using System;
using UnityEditor;
using UI.Li.Editor;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;

namespace UI.Li.Json
{
    [CustomPropertyDrawer(typeof(SerializableJson))]
    public class JsonPropertyDrawer: ComposablePropertyDrawer
    {
        protected override IComposition Layout(SerializedProperty property)
        {
            object value = property.GetUnderlyingValue();
            
            if (value is not SerializableJson json)
                throw new InvalidOperationException($"This drawer can not handle {value.GetType().FullName}");

            void UpdateValue(JToken v)
            {
                json.Value = v;
                PrefabUtility.RecordPrefabInstancePropertyModifications(property.serializedObject.targetObject);
            }
              
            return CustomField.V(
                property,
                JsonUI.DynamicTypeValue(json.Value, UpdateValue)
            );
        }
    }
}